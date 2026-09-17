module Spec

open System
open System.Text.Json
open EasyBuild.FileSystemProvider
open FSharp.Compiler.IO
open Fake.Core
open Fake.Core.Context
open Partas.GitNet
open Partas.Tools.SepochSemver
open Partas.TypeProvider.BuildHelper
open Partas.Build
open Partas.TypeProvider.BuildHelper.Runtime.Project

[<Literal>]
let __ROOT_DIRECTORY__ = __SOURCE_DIRECTORY__ + "/.."
/// Files/paths that are not guaranteed (may be cleaned out of existence)
[<Literal>]
let __VIRTUAL_DIRECTORY__ = "
    fsdocs/
    temp/
        electron-api.json
    ci/
        cache.json
    bin/
"
/// Type provider which provides design time hints for runtime values
type Repo = BuildHelperProvider<__ROOT_DIRECTORY__, __VIRTUAL_DIRECTORY__, capabilityFullOverride = true>

/// The shape of data returned from <c>gh</c> cli when we request release info in json
[<Struct>]
type ReleaseInfo = {
    createdAt: System.DateTime
    isLatest: bool
    isPrerelease: bool
    tagName: string
}
/// <c>&lt;owner>/&lt;repo_name></c>
[<Struct>]
type Repository = {
    Owner: string
    Name: string
}
/// <summary>Flags that modify the execution of downloading assets using the <c>gh</c> cli</summary>
type DownloadFlags = {
    Overwrite: bool
    SkipExisting: bool
    Output: string option
}
/// <summary>
/// The set of options for executing a download using <c>gh</c> cli
/// </summary>
type DownloadArgs = {
    Repository: Repository
    Release: ReleaseInfo option
    Pattern: string list
    Directory: string option
    Flags: DownloadFlags
}
/// <summary>
/// The set of options for creating a pr using the <c>gh</c> cli
/// </summary>
type PrCreateArgs =
    { Assignee: string list
      Base: string option
      Body: string option
      BodyFile: string option
      Draft: bool
      DryRun: bool
      Fill: bool
      FillFirst: bool
      FillVerbose: bool
      Head: string option
      Label: string list
      Milestone: string option
      NoMaintainerEdit: bool
      Project: string list
      Recover: string option
      Reviewer: string list
      Template: string option
      Title: string option
      Repo: Repository }

    static member Init =
        { Assignee = []
          Base = None
          Body = None
          BodyFile = None
          Draft = false
          DryRun = false
          Fill = false
          FillFirst = false
          FillVerbose = false
          Head = None
          Label = []
          Milestone = None
          NoMaintainerEdit = false
          Project = []
          Recover = None
          Reviewer = []
          Template = None
          Title = None
          Repo = { Owner = ""; Name = "" } }

module Versions =
    /// The calculated change in version of a semver
    type DeltaKind =
        | Major
        | Minor
        | Patch
        | Equal
/// <summary>
/// The different version sources used when calculating
/// the semver change type and value for electron.
/// </summary>
type ElectronDeltaVersions = {
    /// <summary>
    /// Our <c>Fable.Electron</c> version (the nuget value)
    /// </summary>
    FableElectronPackage: Semver.SemVersion voption
    /// <summary>
    /// The version of electron that <c>Fable.Electron</c>
    /// was calculated against (can be different to our nuget version).
    /// </summary>
    FableElectronElectron: Semver.SemVersion voption
    /// <summary>
    /// The version of electron that we have in ci/cache.json
    /// </summary>
    CachedElectron: Semver.SemVersion voption
    /// <summary>
    /// The version of electron we have downloaded to generate against.
    /// </summary>
    DownloadedElectron: Semver.SemVersion voption
}

/// <summary>
/// Vectors of semver version for electron
/// </summary>
type ElectronDelta = {
    Versions: ElectronDeltaVersions
    Dirty: bool
    Major: int
    Minor: int
    Patch: int
}

module Projects =
    let allProjects = Repo.Project.AllProjects()
    /// Only projects located under src/
    let srcProjects = allProjects |> List.filter _.RelativePath.Contains("src")
    /// Only projects located under tests/
    let testProjects = allProjects |> List.filter _.RelativePath.Contains("tests")
    /// Only projects with 'Fable' in their name
    let fableProjects = allProjects |> List.filter _.Name.Contains("Fable")
    /// Only the docs project
    let docs = allProjects |> List.find _.Name.Equals("Docs")
    /// Only the build project
    let build = allProjects |> List.find _.Name.Equals("Build")

module GitNet =
    /// Configuration for GitNet
    let config = {
        GitNetConfig.initFSharp with
            RepositoryPath = Repo.FileSystem.``.``.FullName
            // These are the projects that are ignored for versioning
            // The GitNet system won't bother matching commits to files under their directories
            Projects.IgnoredProjects = Projects.allProjects |> List.except Projects.srcProjects |> List.map _.Name
            // Whether gitnet should generate assembly files
            AssemblyFiles = AssemblyFileManagement.None
            // Whether gitnet should write updated versions to the project files directly
            WriteVersionToProjects = true
            Bump.DefaultBumpStrategy = ForceBumpStrategy.All
            // When you make a conventional commit the type is the first verb/adjective: "<type>[(<scope>)]: <summary>"
            // GitNet will match commits to the 'type' of bump they should cause.
            // There are different matching methods, the easiest to use is the 'type'.
            Bump.Mapping.Major =
                // This means any conventional commit starting with "breaking" or "remove" will cause
                // a major bump for the projects that had modified files in that commit
                [ BumpMatcher.Type "breaking"; BumpMatcher.Type "remove" ]
            Bump.Mapping.Minor = [ BumpMatcher.Type "feat"; BumpMatcher.Type "new"; BumpMatcher.Type "add" ]
            Bump.Mapping.Patch = [ BumpMatcher.Type "fix"; BumpMatcher.Type "update"; BumpMatcher.Type "change" ]
            // Whether the output changelog/release-notes will include non-conventional commits
            Output.AllowUnconventional = false
            // The output changelog/release-notes groups commits into different categories.
            // This is where that is controlled
            Output.GroupMatcher = [
                GroupMatcher(CommitGroup.Defaults.breaking, [ BumpMatcher.Type "breaking" ])
                GroupMatcher(CommitGroup.Defaults.changed, [ BumpMatcher.Type "update"; BumpMatcher.Type "change" ])
                GroupMatcher(CommitGroup.Defaults.deprecated, [
                    BumpMatcher.Type "depr"
                    BumpMatcher.Type "deprecated"
                    BumpMatcher.Type "deprecate"
                ])
                GroupMatcher(CommitGroup.Defaults.feat, [
                    BumpMatcher.Type "feat"
                    BumpMatcher.Type "enhancement"
                    BumpMatcher.Type "new"
                    BumpMatcher.Type "added"
                ])
                GroupMatcher(CommitGroup.Defaults.fix, [ BumpMatcher.Type "fix"; BumpMatcher.Type "fixed" ])
                GroupMatcher(CommitGroup.Defaults.revert, [
                    BumpMatcher.Type "rollback"
                    BumpMatcher.Type "revert"
                    BumpMatcher.Type "rev"
                ])
                GroupMatcher(CommitGroup.Defaults.changed, [
                    BumpMatcher.Type "updated"
                    BumpMatcher.Type "update"
                    BumpMatcher.Type "change"
                ])
            ]
            // Whether commits that don't match into a group from above will be included IN THE OUTPUT
            Output.AllowUnmatched = true
            // Any commit that doesn't match into a group from above will fall back into this group
            Output.DefaultUnmatchedGroup = CommitGroup.Defaults.other
            // Any commit that matches against the filters here will be ignored IN THE OUTPUT
            Output.Ignore = [
                IgnoreCommit.FooterKeyValue("changelog", "true")
                IgnoreCommit.FooterKeyValue("gitnet", "ignore")
                IgnoreCommit.SkipCi
            ]
    }
    let runtime = new GitNetRuntime(config)
    /// We do a gitnet dry run - this doesn't write any versions to files, make any commits/tags etc
    /// and provides us the calculated changes that would have occurred
    let initialCompute = runtime.DryRun()
    let private getInitialVersion scope =
        match initialCompute.Versions.TryGetValue scope  with
        | true, semver -> semver |> ValueOption.bind GitNetTag.chooseSemverCompatible
        | _ -> ValueNone
    let private getInitialBump scope =
        match initialCompute.Bumps.TryGetValue scope with
        | true, semver -> ValueSome semver
        | _ -> ValueNone
    let private getInitial scope =
        {| version = getInitialVersion scope; bump = getInitialBump scope |}
    /// The computed version and bump for Fable.Electron.Forge with the current
    /// git commit history (BEFORE any CI operation has run)
    let initForge = getInitial "Forge"
    /// The computed version and bump for Fable.Electron with the current
    /// git commit history (BEFORE any CI operation has run)
    let initElectron = {|
        version =
            match Semver.SemVersion.TryParse Repo.Project.``Fable.Electron``.Version with
            | true, version -> Some version
            | _ -> None
        bump = getInitialBump "Electron"
    |}
    /// The computed version and bump for Fable.Electron.Remoting with the current
    /// git commit history (BEFORE any CI operation has run)
    let initRemoting = getInitial "Remoting"
    /// Creates a fresh gitnet runtime.
    let createRuntime() = new GitNetRuntime(config)


module Repository =
    /// Run a Cmd, if exitcode 0 then return concat output; otherwise return concat error
    let runCmd cmd = task {
        let! result = Cmd.run ValueNone Map.empty cmd
        if result.exitCode <> 0 then return result.error |> String.concat "\n" |> Error
        else return result.output |> String.concat "\n" |> Ok
    }
    /// Asynchronously gets the release list for the given repository using the github cli
    let getReleaseList repo = task {
        let! result =
            cmd $"gh release list -R {repo.Owner}/{repo.Name} --json tagName,createdAt,isLatest,isPrerelease"
            |> runCmd
        return
            result
            |> Result.map (
                JsonSerializer.Deserialize<ReleaseInfo[]>
                >> Array.toList
                )
    }
    /// Asynchronously completes the download asset operation using the given options through the gh/github cli
    let downloadReleaseAsset (args: DownloadArgs -> DownloadArgs) = task {
        let args = args {
            Repository = { Owner = ""; Name = "" }
            Release = None
            Pattern = []
            Directory = None
            Flags = {
                Overwrite = false
                SkipExisting = false
                DownloadFlags.Output = None
            }
        }
        let hasRepositoryValue =
            args.Repository
            |> function { Owner = ""; Name = "" } -> false | _ -> true
        let! result =
            Cmd.create "gh" "release download"
            |> Cmd.argWhenSome args.Release (_.tagName >> List.singleton)
            |> Cmd.argIf hasRepositoryValue [ "-R" ;$"{args.Repository.Owner}/{args.Repository.Name}" ]
            |> Cmd.args (args.Pattern |> List.collect (fun p -> [ "-p"; p ]))
            |> Cmd.argWhenSome args.Directory (fun dir -> [ "--dir"; dir])
            |> Cmd.argWhenSome args.Flags.Output (fun o -> [ "--output"; o ])
            |> Cmd.argIf args.Flags.Overwrite [ "--clobber" ]
            |> Cmd.argIf args.Flags.SkipExisting [ "--skip-existing" ]
            |> runCmd
        // We are only concerned with whether the download succeeded, so we can ignore
        // whatever was in output
        return Result.map ignore result
    }
    /// Create a PR using the given options asynchronously through the gh/github cli
    let createPr (args: PrCreateArgs -> PrCreateArgs) = task {

        let this = args PrCreateArgs.Init
        let hasRepositoryValue =
            this.Repo
            |> function { Owner = ""; Name = "" } -> false | _ -> true
        let! result =
            Cmd.create "gh" "pr create"
            |> Cmd.argIf (not this.Assignee.IsEmpty) [ "--assignee"; this.Assignee |> String.concat "," ]
            |> Cmd.argWhenSome this.Base (fun b -> [ "--base"; b ])
            |> Cmd.argWhenSome this.BodyFile (fun bodyFile -> [ "--body-file"; bodyFile ])
            |> Cmd.argIf this.Draft [ "--draft" ]
            |> Cmd.argIf this.DryRun [ "--dry-run" ]
            |> Cmd.argIf this.Fill [ "--fill" ]
            |> Cmd.argIf this.FillFirst [ "--fill-first" ]
            |> Cmd.argIf this.FillVerbose [ "--fill-verbose" ]
            |> Cmd.argWhenSome this.Head (fun head -> [ "--head"; head ])
            |> Cmd.args (this.Label |> List.collect (fun l -> [ "--label"; l ]))
            |> Cmd.argWhenSome this.Milestone (fun milestone -> [ "--milestone"; milestone ])
            |> Cmd.argIf this.NoMaintainerEdit [ "--no-maintainer-edit" ]
            |> Cmd.argIf (not this.Project.IsEmpty) [ "--project"; this.Project |> String.concat "," ]
            |> Cmd.argWhenSome this.Recover (fun recover -> [ "--recover"; recover ])
            |> Cmd.argIf (not this.Reviewer.IsEmpty) [ "--reviewer"; this.Reviewer |> String.concat "," ]
            |> Cmd.argWhenSome this.Template (fun template -> [ "--template"; template ])
            |> Cmd.argWhenSome this.Title (fun title -> [ "--title"; title ])
            |> Cmd.argIf hasRepositoryValue [ "--repo"; $"{this.Repo.Owner}/{this.Repo.Name}" ]
            |> runCmd
        return result |> Result.map ignore
    }

module Electron =
    let repo = {
        Owner = "electron"
        Name = "electron"
    }
    /// Asynchronously get the electron release list
    let getReleases() = Repository.getReleaseList repo
    /// Asynchronously download the electron api json for the given release
    let downloadElectronApi overwrite outputFile release =
        Repository.downloadReleaseAsset (fun args -> {
            args with
                Repository = repo
                Pattern = [ "electron-api.json" ]
                Release = Some release
                Flags.Output = Some outputFile
                Flags.SkipExisting = not overwrite
                Flags.Overwrite = overwrite
        })

module ElectronDelta =
    /// Determine the delta kind from a given electron delta
    let deltaKind = function
        | { Major = 0; Minor = 0; Patch = 0 } -> Versions.Equal
        | { Major = 0; Minor = 0 } -> Versions.Patch
        | { Major = 0 } -> Versions.Minor
        | _ -> Versions.Major
    /// Whether the electron delta represents a bump of any kind (patch/minor/major etc)
    let isBump = deltaKind >> _.IsEqual >> not
    /// Whether the electron delta is calculated from a run that occurred outside
    /// the current branch it is running in
    let isProbablyPulled delta =
        let deltaKind = deltaKind delta
        let cachedVersion =
            delta.Versions.CachedElectron
            |> ValueOption.defaultValue (Semver.SemVersion(0,1,0))
        let currentVersion =
            delta.Versions.FableElectronElectron
            |> ValueOption.defaultValue (Semver.SemVersion(0,1,0))
        deltaKind.IsMajor
        && currentVersion.ComparePrecedenceTo(cachedVersion) > 0
    /// What the next tag/version we release for electron. Remembering that
    /// any time we need to release a change for a given electron version, it will
    /// result in only a patch bump. Otherwise, the version follows the electron version
    /// it was generated from.
    let nextElectronVersion delta =
        let deltaKind = deltaKind delta
        let makeSepochSemver = fun semver ->
            {
                SemVer = ValueOption.defaultValue (Semver.SemVersion(0,1,0)) semver
                Sepoch = Sepoch.Scope "Electron"
            }
        match delta, deltaKind with
        | _, (Versions.Major | Versions.Minor) -> delta.Versions.DownloadedElectron |> makeSepochSemver
        | _, Versions.Patch
        | { Dirty = true }, _ -> delta.Versions.FableElectronPackage |> makeSepochSemver |> SepochSemver.bumpPatch
        | _ -> delta.Versions.FableElectronPackage |> makeSepochSemver

/// CLI flags/options for running operations in this repository/project
module Options =
    let quick =
        Input.option<bool> "--quick"
        |> Input.alias "-q"
        |> Input.desc "Skips installation, cleaning, and/or download steps."
    let release =
        Input.optionMaybe<string> "--release"
        |> Input.desc "Specify the release to target for this action."
        |> Input.arity ExactlyOne
        |> InputSpec.ofInput
        |> InputSpec.map (Option.map SemVer.parse)
    let cache =
        Input.option<bool> "--cache-release"
        |> Input.desc "Target the release specified in the cache"
    let watch =
        Input.option<bool> "--watch"
        |> Input.alias "-w"
        |> Input.desc "Run the operation in watch mode."
    let openFlag =
        Input.option<bool> "--windowed"
        |> Input.desc "Open the test project as an electron application"
    let npmCi =
        Input.option<bool> "--clean-install"
        |> Input.alias "--npm-ci"
        |> Input.desc "Install operations with npm are performed using --clean-install"
    let config =
        Baked.Dotnet.config.option
        |> InputSpec.ofInput
        |> InputSpec.map (Option.defaultValue "Release")
    let allProjectTargets =
        Input.option<ProjectRef list> "--project"
        |> Input.alias "-p"
        |> Input.desc "Project(s) to target with operation. Defaults depend on operation in question, but will be some subset of all projects."
        |> Input.mapFromManyWith StringComparer.OrdinalIgnoreCase (
            Projects.allProjects
            |> List.map (fun project ->
                project.Name, project
            )
            )
        |> Input.arity OneOrMore
        |> Input.allowMultipleArgumentsPerToken
        |> Input.def []
    let concurrent =
        Input.option<bool> "--parallel"
        |> Input.def true
        |> Input.desc "Whether to perform operations in parallel"

module Inputs =
    /// Reads and returns the cached release info
    let cache = input {
        return
            if Repo.VirtualFileSystem.ci.``cache.json``.Exists |> not then None else
            use reader = Repo.VirtualFileSystem.ci.``cache.json``.OpenRead()
            reader.ReadAllText()
            |> JsonSerializer.Deserialize<ReleaseInfo>
            |> Some
    }
    /// asynchronously retrieves the release info for either the requested release/bump type
    /// from the electron repo using the gh cli. consider this to be the requested release info
    /// we are generating against. Falls back to the 'latest' release if none are specifically
    /// requested.
    let release = input {
        let! release = Options.release
        and! patchOnly =
            Input.option<bool> "--only-patch"
        and! minorOnly =
            Input.option<bool> "--only-minor"
        and! cache = cache
        and! cacheRelease = Options.cache
        let cache = cache |> Option.map (_.tagName.Trim('v') >> SemVer.parse)
        let task = Electron.getReleases()
        task.RunSynchronously()
        let result = task.Result
        match result with
        | Error value ->
            return failwith value
        | Ok releases ->
            if (patchOnly || minorOnly) && cache.IsNone then
                failwith "--only-minor && --only-patch can only be used when there is a cached release value. \
                        Either specify a release to operate on with --release or allow the latest release to download"
            elif cacheRelease && cache.IsNone then
                failwith "--cache-release can only be used when there is a cached release object @ /ci/cache.json"
            return
                releases
                |> List.filter (_.isPrerelease >> not)
                |> List.filter (function
                    | info when release.IsSome ->
                        release
                        |> Option.exists (
                            _.AsString
                            >> (=) (info.tagName.Trim('v'))
                            )
                    | info when cache.IsSome && cacheRelease ->
                        SemVer.parse (info.tagName.Trim('v'))
                        |> (=) cache.Value
                    | info when patchOnly ->
                        let value = SemVer.parse (info.tagName.Trim('v'))
                        value.Major = cache.Value.Major
                        && value.Minor = cache.Value.Minor
                        && value.Patch > cache.Value.Patch
                    | info when minorOnly ->
                        let value = SemVer.parse (info.tagName.Trim('v'))
                        value.Major = cache.Value.Major
                        && value.Minor > cache.Value.Minor
                    | info when (minorOnly && patchOnly) ->
                        let value = SemVer.parse (info.tagName.Trim('v'))
                        value.Major = cache.Value.Major
                        && (
                            value.Minor > cache.Value.Minor
                            || (value.Minor = cache.Value.Minor
                                && value.Patch > cache.Value.Patch)
                            )
                    | info -> info.isLatest
                )
                |> function
                    | [] when release.IsSome ->
                        failwith $"Unable to find the specified release {release.Value.AsString}"
                    | [] -> None
                    | values -> List.maxBy _.createdAt values |> Some
    }
    /// Asynchronously downloads the electron api file for the requested release
    let downloadApi = input {
        let! release = release
        match release with
        | None -> return ()
        | Some release ->
        let task =
            Electron.downloadElectronApi
                false
                (Repo.VirtualFileSystem.temp.``electron-api.json``.ToString())
                release
        task.RunSynchronously()
        return
            task.Result
            |> Result.mapError failwith
            |> Result.iter ignore
    }
    /// Asynchronously calculates the electron delta versions using the
    /// requested release, cache, and project values
    let electronDeltaVersions = input {
        let! cache = cache
        and! release = release
        return {
            FableElectronPackage =
                match Repo.Project.``Fable.Electron``.Version with
                | "" -> ValueNone
                | version -> Semver.SemVersion.Parse version |> ValueSome
            FableElectronElectron =
                match Repo.Project.``Fable.Electron``.Property("ElectronVersion") with
                | "" -> ValueNone
                | version -> Semver.SemVersion.Parse version |> ValueSome
            CachedElectron =
                cache
                |> Option.map (
                    _.tagName.Trim('v')
                    >> Semver.SemVersion.Parse
                    )
                |> Option.toValueOption
            DownloadedElectron =
                release
                |> Option.map (
                    _.tagName.Trim('v')
                    >> Semver.SemVersion.Parse
                    )
                |> Option.toValueOption
        }
    }

    /// Checks whether the generated file is modified using git
    let electronIsDirty() =
        let fileIsDirty file =
            Repo.Git.Run($"status -s -- {file}")
            |> function
                | "" -> false
                | v -> true
        Repo.Git.IsDirty()
        && fileIsDirty Repo.FileSystem.src.``Fable.Electron``.``Program.fs``
    /// calculated electron delta versions at request time
    let electronDelta = input {
        let! deltaVersions = electronDeltaVersions
        let defaultInt = ValueOption.defaultValue 0
        let deltaPath (path: Semver.SemVersion -> bigint) =
            (deltaVersions.DownloadedElectron
            |> ValueOption.map (path >> int)
            |> defaultInt) - (
            deltaVersions.FableElectronElectron
            |> ValueOption.map (path >> int)
            |> defaultInt
            )
        return {
            Versions = deltaVersions
            Dirty = electronIsDirty()
            Major = deltaPath _.Major
            Minor = deltaPath _.Minor
            Patch = deltaPath _.Patch
        }
    }

    let initialGitStatus = input {
        let! electronDelta = electronDelta
        let forge, remoting = GitNet.initForge, GitNet.initRemoting
        let anyPackageUpdated =
            forge.bump.IsSome
            || remoting.bump.IsSome
            || ElectronDelta.isBump electronDelta
        let requiresPull =
            not (ElectronDelta.isProbablyPulled electronDelta)
            && (ElectronDelta.deltaKind electronDelta).IsMajor
        return {| delta = electronDelta; forge = forge; remoting = remoting; anyPackageUpdated = anyPackageUpdated; requiresPull = requiresPull  |}
    }

module Stage =
    open Fake.IO
    open Fake.IO.Globbing.Operators
    let checkElectronRepoHealth = stage "check electron repo connection" {
        runHttpHealthCheck $"https://github.com/{Electron.repo.Owner}/{Electron.repo.Name}"
    }
    let echoCurrentGitStatus = input {
        let! initialGitStatus = Inputs.initialGitStatus
        let str = $"
Summary of current status for GitNet:

Electron Cached Version: {initialGitStatus.delta.Versions.CachedElectron.ToString()}
    This is the electron release
    information that is stored in
    ci/cache.json

Is Probably Pulled: {ElectronDelta.isProbablyPulled initialGitStatus.delta}
    We can assume the repository
    is being merged from a pull when
    the electron cached version is higher
    than the project files electron version

Current Electron Version: {initialGitStatus.delta.Versions.FableElectronElectron.ToString()}
    This is the project file electron version.
    This is not updated except when being merged to main.

Current Package Version: {initialGitStatus.delta.Versions.FableElectronPackage.ToString()}
    This is the package version for Fable.Electron

Downloaded Version: {initialGitStatus.delta.Versions.DownloadedElectron.ToString()}
    This is the version of Electron that was
    downloaded in this run.

If the major is updated, then we will submit a pull:
    {ElectronDelta.deltaKind initialGitStatus.delta}

Next version: {ElectronDelta.nextElectronVersion initialGitStatus.delta |> _.ToString()}
    This is the next calculated version
    of Fable.Electron

Is Electron Package Updated: {ElectronDelta.isBump initialGitStatus.delta}
    Whether or not the Electron package
    is changed, regardless of whether the
    'electron' version has changed.
    This is caused by changes in the generator.

Is Any Package Updated: {initialGitStatus.anyPackageUpdated}
    Whether any of our packages have
    changed.

Next versions:
    Remoting: {initialGitStatus.remoting}
    Forge: {initialGitStatus.forge}

Package Requires Pull: {initialGitStatus.requiresPull}
    Whether this run will result in a pull.
"
        return stage "echo initial GitStatus" {
            echo str
        }
    }
    /// Clean the temp/ directory
    let cleanTemp = input {
        let! quick = Options.quick
        return stage "clean-temp" {
            when' (not quick)
            run (fun _ -> Shell.cleanDir "temp/")
        }
    }
    /// Clean bin/output directories
    let clean = input {
        let! quick = Options.quick
        and! cleanTemp = cleanTemp
        return stage "clean" {
            when' (not quick)
            parallel'
            cleanTemp
            run (fun _ ->
                !!"**/**/bin"
                -- "bin"
                |> Shell.cleanDirs
            )
        }
    }
    /// Clean any generated fable files.
    /// Only operates in fable project directories outside tests/.
    let cleanFable = input {
        let! quick = Options.quick
        return stage "clean-fable" {
            when' (not quick)
            parallel'
            for project in Projects.fableProjects |> List.except Projects.testProjects do
            stage $"clean-{project.Name}" { run "dotnet fable clean -e .js --yes" }
        }
    }
    /// runs dotnet tool restore
    let restoreTools = input {
        let! quick = Options.quick
        return stage "restore-tools" {
            when' (not quick)
            run "dotnet tool restore"
        }
    }
    /// Performs npm install operations for tests
    let installTests = input {
        let! cleanInstall = Options.npmCi
        and! quick = Options.quick
        return stage "test install" {
            workingDir Repo.Project.``Fable.Electron.Remoting.Tests``.Directory
            when' (not quick)
            run (
                Cmd.create "npm" "install"
                |> Cmd.argIf cleanInstall [ "--clean-install" ]
            )
        }
    }
    /// Installs npm tests, and runs them.
    /// Can optionally run the tests in watch mode, or open the electron application window.
    let runTest = input {
        let! installTests = installTests
        and! watch = Options.watch
        and! windowed = Options.openFlag
        return stage "test" {
            installTests
            run (
                Cmd.create "npm" "run"
                |> Cmd.arg (
                    if watch then "watch"
                    elif windowed then "start"
                    else "test"
                    )
            )
        }
    }
    /// Generates electron-api for the downloaded api
    let generate = input {
        let! downloadedApi = Inputs.downloadApi
        if not Repo.VirtualFileSystem.temp.``electron-api.json``.Exists then
            failwith $"electron API data was not found @ {Repo.VirtualFileSystem.temp.``electron-api.json``.FullName}"
        ElectronApi.Json.Parser.Generator.Transpiler.generateFromApiFile
            Repo.VirtualFileSystem.temp.``electron-api.json``.FullName
            Repo.FileSystem.src.``Fable.Electron``.``Program.fs``.FullName
        return ()
    }
    /// Writes the given releaseInfo to the ci/cache.json file
    let writeToCache (releaseInfo: InputSpec<ReleaseInfo>) = input {
        let! releaseInfo = releaseInfo
        releaseInfo
        |> JsonSerializer.Serialize
        |> File.writeString false Repo.VirtualFileSystem.ci.``cache.json``.FullName
        return ()
    }
    /// Builds projects
    let build = input {
        let! projects =
            Options.allProjectTargets
            |> InputSpec.ofInput
            |> InputSpec.map (function
                | [] -> Projects.srcProjects
                | projects -> projects
                )
        and! config = Options.config
        and! concurrent = Options.concurrent
        return stage "build" {
            failIfNoActiveSubStage
            parallel' concurrent
            for project in projects do
            stage $"build {project.Name}" {
                when' (System.IO.File.Exists project.Path)
                run (
                    Cmd.create "dotnet" "build -v q"
                    |> Cmd.args [ "-c"; config ]
                    |> Cmd.args [ "--project"; project.Path ]
                )
            }
        }
    }
    /// Builds fable projects
    let fableBuild = input {
        let! projects =
            Options.allProjectTargets
            |> InputSpec.ofInput
            |> InputSpec.map (function
                | [] -> Projects.fableProjects |> List.filter _.Name.Equals("Fable.Electron")
                | projects ->
                    Set.union (Set.ofList projects) (Set.ofList Projects.fableProjects)
                    |> Set.toList
                )
        and! config = Options.config
        and! concurrent = Options.concurrent
        return stage "fable build" {
            parallel' concurrent
            for project in projects do
            stage project.Name {
                workingDir project.Directory
                run $"dotnet fable -e .js -c {config} --noCache"
            }
        }
    }

    /// Packs projects into /bin/
    let pack = input {
        let! projects =
            Options.allProjectTargets
            |> InputSpec.ofInput
            |> InputSpec.map (function
                | [] -> Projects.srcProjects
                | projects -> projects
                )
        and! concurrent = Options.concurrent
        return stage "pack" {
            failIfNoActiveSubStage
            parallel' concurrent
            for project in projects do
            stage $"pack {project.Name}" {
                when' (System.IO.File.Exists project.Path)
                run (
                    Cmd.create "dotnet" "pack -v q --no-build --no-restore"
                    |> Cmd.args [ "--project"; project.Path ]
                    |> Cmd.args [ "-o"; Repo.VirtualFileSystem.bin.``.``.FullName ]
                )
            }
        }
    }
    /// Publishes all packed packages in /bin/ to nuget
    let publish = input {
        let! apiKey = Baked.NuGet.apiKey.option
        return stage "publish" {
            whenSome apiKey (fun key ->
                let cmd =
                    Cmd.create "dotnet" "nuget push"
                    |> Cmd.arg (System.IO.Path.Combine(Repo.VirtualFileSystem.bin.``.``.FullName, "*.nupkg"))
                    |> Cmd.args [
                        "--source"
                        "https://api.nuget.org/v3/index.json"
                        "--skip-duplicate"
                    ]
                    |> Cmd.secretOption "--api-key" key
                stage "execute" {
                    run cmd
                }
            )
        }
    }

    let gitnet = pipeline "gitnet" {
    }

module Commands =
    let build = command "build" {
        description "Build projects"
        Stage.restoreTools
        Stage.clean
        Stage.build
    }

    let format = command "format" {
        description "Format files"
    }

    let fable = command "fable" {
        description "Operations specifically for fable projects in the repository"
        command "clean" {
            Stage.restoreTools
            Stage.cleanFable
        }
        command "build" {
            Stage.restoreTools
            Stage.cleanFable
            Stage.fableBuild
        }
        command "test" {
            Stage.restoreTools
            Stage.clean
            Stage.cleanFable
        }
    }

    let releases = command "releases" {
        description ""
    }

    let docs = command "docs" {
        description ""
    }





//%FileProvider%START%
type Root = AbsoluteFileSystem<__ROOT_DIRECTORY__>

type VirtualRoot =
    VirtualFileSystem<
        __ROOT_DIRECTORY__,
        """
fsdocs/
temp
    electron-api.json
"""
     >
//%FileProvider%END% //%PredefinedFileProvider%START%
module Projects =
    module Folders =
        type Remoting = Root.src.``Fable.Electron.Remoting``
        type Generator = Root.src.``ElectronApi.Json.Parser``
        type Build = Root.ci
        type Electron = Root.src.``Fable.Electron``
        type Forge = Root.src.``Fable.Electron.Forge``
        type Tests = Root.tests

    let Remoting = Folders.Remoting.``Fable.Electron.Remoting.fsproj``
    let Generator = Folders.Generator.``ElectronApi.Json.Parser.fsproj``
    let Build = Root.``Build.fsproj``
    let Electron = Folders.Electron.``Fable.Electron.fsproj``
    let Forge = Folders.Forge.``Fable.Electron.Forge.fsproj``

    let Test =
        Folders.Tests.``Fable.Electron.Remoting.Tests``.``Fable.Electron.Remoting.Tests.fsproj``

    let BuildTest = Folders.Tests.``Build.Tests``.``Build.Tests.fsproj``

    let Docs = Root.docs.``Docs.fsproj``

module Solutions =
    let Electron = Root.``Fable.Electron.sln``

module Files =
    let Api = VirtualRoot.temp.``electron-api.json``
    let Cache = Root.ci.``cache.json``
//%PredefinedFileProvider%END% //%TargetsExample%START%
module Ops =
    /// Clean directories from build material, and temporary files downloaded such as electron-api.json
    [<Literal>]
    let clean = "clean"

    /// Clean directories from fable generated files
    [<Literal>]
    let fableClean = "fable-clean"

    /// List releases from electron
    [<Literal>]
    let listReleases = "list-releases" //%TargetsExample%END%

    /// List releases from electron with details
    [<Literal>]
    let listDetailedReleases = "list-detailed-releases"
    //%DownloadTargets%START%
    /// Download a specified release
    [<Literal>]
    let downloadApi = "download-api"

    [<Literal>]
    let downloadLatest = "download-latest"

    /// Combines list releases and download api interactively
    [<Literal>]
    let downloadInput = "download-input" //%DownloadTargets%END%

    /// Post download cleanup
    [<Literal>]
    let postDownload = "post-download-clean"

    /// Generate the Fable.Electron bindings
    [<Literal>]
    let generate = "generate"

    [<Literal>]
    let activateGitnet = "activate-gitnet"

    /// Setup docs via npm i or npm ci
    [<Literal>]
    let setupDocs = "setup-docs"

    /// Run docs in watch mode
    [<Literal>]
    let docs = "docs"

    /// Build projects
    [<Literal>]
    let build = "build"

    /// Pack projects
    [<Literal>]
    let pack = "pack"

    /// Push to nuget
    [<Literal>]
    let push = "push"

    /// Generate the API Docs (only to be run in an external repo)
    [<Literal>]
    let generateApiDocs = "generate-api-docs"

    /// Does setup for tests by downloading deps with npm i or npm ci
    [<Literal>]
    let setupTest = "setup-test"

    /// Run tests
    [<Literal>]
    let test = "test"

    /// Do post test cleanup
    [<Literal>]
    let postTest = "post-test"

    /// Restores tools in repo
    [<Literal>]
    let restore = "restore"

    /// Formats files with fantomas
    [<Literal>]
    let format = "format"

    /// Cron job for use by CI
    [<Literal>]
    let cron = "cron"

    [<Literal>]
    let loadCache = "load-cache"

    [<Literal>]
    let gitnet = "gitnet"

    [<Literal>]
    let downloadCache = "download-cache"

    [<Literal>]
    let buildTool = "build-tool"

module FlagArgs =
    module Common =
        [<Literal>]
        let release = "--release"

        [<Literal>]
        let nugetApi = "--nuget-key"

        [<Literal>]
        let ghKey = "--gh-key"

    module Run =
        [<Literal>]
        let target = "--target"

module Flags =
    module Cron =
        [<Literal>]
        let downloadMinorOnly = "--only-minor"

        [<Literal>]
        let downloadPatchOnly = "--only-minor"

    module Test =
        [<Literal>]
        let open' = "--open"

        [<Literal>]
        let watch = "--watch"

    module Common =
        [<Literal>]
        let help = "--help"

        [<Literal>]
        let detailed = "--detailed"

        [<Literal>]
        let quick = "--quick"

        [<Literal>]
        let dry = "--dry-run" //%ExampleArgsDef%END%

        [<Literal>]
        let npmCi = "--npm-ci"

        [<Literal>]
        let skipTest = "--skip-test"

        [<Literal>]
        let debug = "--debug" //%ExampleCommandsDef%START%

module Commands =
    [<Literal>]
    let docs = "docs"

    [<Literal>]
    let test = "test"

    [<Literal>]
    let format = "format"

    [<Literal>]
    let generateApiDocs = "generate-api-docs"

    [<Literal>]
    let generate = "generate" //%ExampleCommandsDef%END%

    [<Literal>]
    let pack = "pack"

    [<Literal>]
    let cron = "cron"

    [<Literal>]
    let run = "run"

    [<Literal>]
    let buildTool = "build-tool"

    [<Literal>]
    let download = "download"


[<Literal>]
let githubUsername = "GitHub Action"

[<Literal>]
let githubEmail = "41898282+github-actions[bot]@users.noreply.github.com"
//%ArgsType%START%
type Args =
    static let mutable args = None

    static let hasFlag value =
        args |> Option.exists (DocoptResult.hasFlag value)

    static let getFlag value =
        args |> Option.bind (DocoptResult.tryGetArgument value)

    static member setArgs argsv =
        args <- (Cli.parser: Docopt).Parse(argsv) |> Some

    static member detailed = hasFlag Flags.Common.detailed
    static member quick = hasFlag Flags.Common.quick
    static member dryRun = hasFlag Flags.Common.dry //%ArgsType%END%
    static member help = hasFlag Flags.Common.help
    static member release = getFlag FlagArgs.Common.release
    static member npmCi = hasFlag Flags.Common.npmCi
    static member skipTest = hasFlag Flags.Common.skipTest
    static member apiKey = getFlag FlagArgs.Common.nugetApi
    static member target = getFlag FlagArgs.Run.target
    static member gitClientToken = getFlag FlagArgs.Common.ghKey
    static member debug = hasFlag Flags.Common.debug
    static member open' = hasFlag Flags.Test.open'
    static member watch = hasFlag Flags.Test.watch
    static member downloadMinorOnly = hasFlag Flags.Cron.downloadMinorOnly
    static member downloadPatchOnly = hasFlag Flags.Cron.downloadPatchOnly

//%CliType%START%
and Cli =
    static member spec =
        $"""
Usage:
    fable-electron [options]
    fable-electron {Commands.docs} [options]
    fable-electron {Commands.download} [options]
    fable-electron {Commands.generate} [options]
    fable-electron {Commands.generateApiDocs} [options]
    fable-electron {Commands.pack} [options]
    fable-electron {Commands.cron} [options] [crons]
    fable-electron {Commands.run} [run] [options] [crons] [test]
    fable-electron {Commands.test} [test] [options]
    fable-electron {Commands.format} [options]
    fable-electron {Commands.buildTool}

Cron Options [crons]:
    --only-minor            Only run a scheduled generation for minor releases of
                            the current electron semver. (can use together with patch)
    --only-patch            Only run a scheduled generation for patch releases of
                            the current electron semver. (can use together with minor)

Test Options [test]:
    --open                  Will run the test application and open the app instead of
                            running the headless test suite.
    --watch                 Will run the test application and open the app in watch mode
                            instead of running the headless test suite.

Run Options [run]:
    --target <NAME>         The target to run

Options [options]:
    -h, --help              Show this help message.
                            Note that the `cron` job should only be performed by the CI runners
    -D, --detailed          When printing release information, show all fields.
    -Q, --quick             Skip setup steps, such as installing dependencies (for local environments).
    --dry-run               Collect actions and print them at the end instead of pushing any changes.
    --npm-ci                `npm install` commands are run using `ci` (clean install) instead. Use this
                            if you are encountering 'module missing' errors for npm dependencies.
    --release <RELEASE>     Perform the actions for the specific release tag.
    --skip-test             Skip tests
    --format                Run fantomas
    --nuget-key <API-KEY>   The key used in authentication to push packages to NuGet.
    --gh-key <PAT>          Personal access token for GitHub to use instead of the CI runner.
    --debug                 Shows the dependency list for the command and args
"""

    static member parser = Docopt(Cli.spec) //%CliType%END%

open Fake.IO.Globbing.Operators

let sourceFiles =
    !!"**/*.fs"
    -- "**/obj/**/*.*"
    -- "**/AssemblyInfo.fs"
    // Fantomas will most assuredly choke on this
    -- "**/Fable.Electron/Program.fs"

// Credit SAFE STACK
let initializeContext () =
    let execContext = FakeExecutionContext.Create false "build.fsx" []
    setExecutionContext (RuntimeContext.Fake execContext)

let createProcess exe args dir =
    CreateProcess.fromRawCommand exe args
    |> CreateProcess.withWorkingDirectory dir
    |> CreateProcess.ensureExitCode

let dotnet args dir =
    createProcess "dotnet" args dir |> Proc.run |> ignore