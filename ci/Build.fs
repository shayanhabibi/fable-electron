module Build.ci.Build

open System.Collections.Generic
open System.IO
open Workers
open System.Text.Json
open EasyBuild.Tools
open EasyBuild.Tools.Git
open Fake.Api
open Fake.Core
open Fake.IO
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
open Spec
open Fake.Tools
open Fake.DotNet
open EasyBuild.Tools.ChangelogGen
open Fake.JavaScript
open Partas.GitNet
open Partas.GitNet.RepoCracker
open Partas.GitNet.BuildHelpers
open GitNet

initializeContext ()

module Status =
    let mutable private _cache = None
    let setCache (releaseInfo: ReleaseInfo) = _cache <- Some releaseInfo
    let hasCache () = _cache.IsSome
    let tryGetCache() = _cache
    let getCache = tryGetCache >> Option.get
    let mutable private _release = None
    let setRelease (release: ReleaseInfo) = _release <- Some release
    let hasRelease () = _release.IsSome
    let tryGetRelease() = _release
    let getRelease = tryGetRelease >> Option.get
    let getSemver = getRelease >> _.tagName.TrimStart('v') >> SemVer.parse
    
    let mutable private _fableElectronNewVersion = None
    let setNewVersion (ver: SemVerInfo) = _fableElectronNewVersion <- Some ver
    let hasNewVersion () = _fableElectronNewVersion.IsSome
    let tryGetNewVersion () = _fableElectronNewVersion
    let getNewVersion = tryGetNewVersion >> Option.get

// Laundry
Target.create Ops.clean (ignore >> Laundry.clean)
Target.create Ops.fableClean (ignore >> Laundry.fableClean)
Target.create Ops.listReleases (fun _ -> Electron.listReleases true)
Target.create Ops.listDetailedReleases (fun _ -> Electron.listReleases false)
Target.create Ops.downloadApi <| fun _ ->
    match Args.release with
    | Some value ->
        Electron.tryGetReleaseFromString value
        |> Option.orElseWith(fun () -> failwith $"Could not download a release matching the input '{value}'")
        |> Option.iter (fun releaseInfo ->
            Status.setRelease releaseInfo
            Electron.downloadRelease releaseInfo)
    | None -> failwithf $"Target %s{Ops.downloadApi} requires the argument '--release <RELEASE>' to be set"
Target.create Ops.downloadInput <| fun _ ->
    let getUserInput () =
        let isQuit: string -> bool = _.ToLowerInvariant() >> function
            | "q" | "quit" -> true
            | _ -> false
        match UserInput.getUserInput "Choose a release or (q)uit:\n" with
        | text when isQuit text ->
            failwith "User quit"
        | text -> text
    let rec run value =
        match Electron.tryGetReleaseFromString value with
        | None ->
            Electron.listReleases true
            getUserInput ()
            |> run
        | Some value ->
            Status.setRelease value
            Electron.downloadRelease value
    Electron.listReleases true
    getUserInput ()
    |> run
Target.create Ops.downloadLatest <| fun _ ->
    Electron.tryGetRelease _.isLatest
    |> function
        | Some release ->
            Status.setRelease release
            Electron.downloadRelease release
        | None ->
            failwith "Was not able to identify the latest release using the 'gh' cli."
Target.create Ops.postDownload (ignore >> Laundry.clean)
Target.create Ops.generate <| fun _ ->
    Electron.generate()
    |> Result.mapError (fun _ ->
        failwith "Attempted to generate from an electron-api.json, but none were downloaded.\n \
                  Either run target 'generate-release' or place the electron-api.json in the \
                  '/temp' folder at the root of the repository directory."
        )
    |> ignore
Target.create Ops.setupDocs <| fun _ -> Docs.setup Args.npmCi
Target.create Ops.docs (ignore >> Docs.dev)
Target.create Ops.build (fun _ -> Project.build Project.Targets.All)
Target.create Ops.pack (fun _ -> Project.pack false Project.Targets.All)
Target.create Ops.push (ignore >> Project.push)
Target.create Ops.generateApiDocs (ignore >> ApiDocs.validateDir >> ApiDocs.build)
Target.create Ops.setupTest (fun _ -> Electron.installTests Args.npmCi)
Target.create Ops.test (ignore >> Electron.test)
Target.create Ops.postTest (ignore >> Laundry.fableClean)
Target.create Ops.gitPrelude (ignore >> Laundry.configGitBot)
Target.create Ops.restore (ignore >> Laundry.restoreTools)
Target.create Ops.format (ignore >> Laundry.format)
Target.create Ops.cron ignore
Target.create Ops.changelogGen <| fun _ ->
    ignore ()
Target.create Ops.loadCache <| fun _ ->
    if File.exists Files.Cache then
        File.readAsString Files.Cache
        |> JsonSerializer.Deserialize<ReleaseInfo>
        |> Status.setCache
let files =
    [ Root.src.``Fable.Electron``.``Fable.Electron.fsproj``
      Root.src.``Fable.Electron``.``Types.fs``
      Root.src.``Fable.Electron``.``Program.fs``

      Root.src.``Fable.Electron.Remoting``.``Fable.Electron.Remoting.fsproj``
      Root.src.``Fable.Electron.Remoting``.``Main.fs``
      Root.src.``Fable.Electron.Remoting``.``Renderer.fs``
      Root.src.``Fable.Electron.Remoting``.``Preload.fs``

      Root.src.``Fable.Electron.Forge``.``Fable.Electron.Forge.fsproj``
      Root.src.``Fable.Electron.Forge``.``Program.fs`` ]
Target.create Ops.testGitNet <| fun para ->
    let isChanged = Electron.isDirty()
    let isElectronVersionDifferent, isMajorChange, isMinorChange =
        match Status.tryGetRelease(), Status.tryGetCache() with
        | Some _, None -> true, true, true
        | Some { tagName = tag1 }, Some { tagName = tag2 } ->
            if tag1 = tag2
            then false,false,false
            else
                let semver1,semver2 =
                    tag1.TrimStart 'v' |> SemVer.parse,
                    tag2.TrimStart 'v' |> SemVer.parse
                true, semver1.Major > semver2.Major, semver1.Minor > semver2.Minor
        | _ -> false, false, false
    match isChanged, isElectronVersionDifferent with
    | false, _ when getInitBumpRemoting.IsNone && getInitBumpForge.IsNone && not para.Context.HasError ->
        Trace.log "No changes during CI."
    | false, _ when not para.Context.HasError ->
        runtime.Run().Bumps
        |> Seq.map _.ToString()
        |> Trace.logItems "The following packages were updated:\n"
        // Target.runSimpleWithContext Ops.push para.Context
        // |> ignore
    | true, true when para.Context.HasError || isMajorChange || isMinorChange ->
        Laundry.createBranch $"ci/electron/{Status.getRelease().tagName}"
        let commitMessage =
            [
                if isMajorChange then
                    "feat!: Electron binding update to match " + Status.getRelease().tagName
                elif isMinorChange then
                    "feat: Electron binding update to match " + Status.getRelease().tagName
                else
                    "fix: Electron binding update to match " + Status.getRelease().tagName
                ""
                "This commit is automatically generated by Build project."
                ""
                ""
            ] |> String.concat "\n"
        Laundry.commitFiles commitMessage files
        let next =
            Status.getRelease()
            |> Changelog.getNextSemVer
        runtime.Run(fun bumps _ ->
            if bumps.ContainsKey "Electron" then
                bumps |> Seq.map(function
                    | KeyValue("Electron", _) -> "Electron", next
                    | KeyValue(key,value) -> key,value)
                |> dict
            else
                seq {
                    yield! bumps
                    KeyValuePair("Electron", next)
                }
                |> Seq.map(fun kv -> kv.Key, kv.Value)
                |> dict
            )
        |> ignore
        Laundry.pushCurrentBranch()
        let title =
            if para.Context.HasError then
                "[GEN ERROR] For "
            else ""
            + "Electron " + Status.getRelease().tagName
        let body =
            if para.Context.HasError then
                "The generation for this build failed and requires \
                some changes to allow tests to pass.
 \
                Once those changes have been made, and tests pass, you can \
                merge this pull."
            else
                "Once you are happy to proceed and tests are passing, you \
                can merge this pull to 'develop' and pull to 'main' whenever \
                you want to publish the packages."
        Laundry.sendPullForDevel title body
    | true, true when not para.Context.HasError ->
        Laundry.commitFiles $"fix: Update bindings; electron {Status.getRelease().tagName}" files
        runtime.Run().Bumps 
        |> Trace.logf "The following packages were updated:\n%A"
    | _ ->
        Trace.logf "No package changes were made"
        
// Target.create Ops.cron <| fun para ->
// Relevant files



open Fake.Core.TargetOperators
// ==========================================================
// CI entry point
[<EntryPoint>]
let main argsv =
    argsv |> Args.setArgs
    // ==========================================================
    // Set what operations of the CI must precede other operations
    let dependencyMapping =
        // Dependency on restore for any tool related actions
        Ops.restore <== [
            Ops.clean
            ==> Ops.fableClean
            Ops.changelogGen
            Ops.downloadApi
            Ops.downloadInput
            Ops.downloadLatest
            Ops.listDetailedReleases
            Ops.listReleases
            Ops.generate
            Ops.generateApiDocs
            Ops.test
            Ops.format
        ]
        Ops.testGitNet <== [
            Ops.postDownload
            Ops.downloadLatest
        ]
            
        [
            // define setup requirements
            Ops.setupTest
            =?> (Ops.test, not Args.quick)
            =?> (Ops.postTest, Args.commit)
            // If generate occurs, it is a soft dependency
            // for multiple targets
            Ops.generate ?==> [
                Ops.test
                Ops.format
                Ops.generateApiDocs
                Ops.changelogGen
                Ops.build
                Ops.pack
                Ops.push
                Ops.testGitNet
                Ops.postDownload
            ]
            // On the other hand, generate has plenty of soft dependencies itself
            Ops.generate <==? [
                Ops.downloadApi
                Ops.downloadInput
                Ops.downloadLatest
            ]
            Ops.generate
            Ops.generate ==> Ops.testGitNet
            Ops.setupDocs
            =?> (Ops.docs, not Args.quick)
            
            Ops.postDownload <==? [
                Ops.downloadApi
                Ops.downloadInput
                Ops.downloadLatest
            ]
            
        ]
    let run =
        if Args.debug then
            Target.printDependencyGraph true
        else
            Target.runOrDefaultWithArguments
    match argsv[0] with
    | _ when Args.help ->
        printfn $"%s{Cli.spec}"
    | Commands.generateApiDocs ->
        run Ops.generateApiDocs
    | Commands.docs ->
        run Ops.docs 
    | Commands.generate ->
        run Ops.generate 
    | Commands.run ->
        match Args.target with
        | None -> failwith "No target supplied to '--target <NAME>'"
        | Some target -> run target
    | Commands.cron ->
        run Ops.cron
    | Commands.pack ->
        run Ops.pack
    | Commands.test ->
        run Ops.postTest
    | maybeTarget ->
        run maybeTarget
    0
