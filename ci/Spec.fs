module Spec

open System.IO
open EasyBuild.FileSystemProvider
open Fake.Core
open Fake.Core.Context
open Fake.IO
open Partas.GitNet

[<Literal>]
let _rootPath = __SOURCE_DIRECTORY__ + "/.."

type Root = AbsoluteFileSystem<_rootPath>

type VirtualRoot =
    VirtualFileSystem<
        _rootPath,
        """
fsdocs/
temp
    electron-api.json
"""
     >

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
    let Test = Folders.Tests.``Fable.Electron.Remoting.Tests``.``Fable.Electron.Remoting.Tests.fsproj``
    let Docs = Root.docs.``Docs.fsproj``

module Solutions =
    let Electron = Root.``Fable.Electron.sln``

module Files =
    let Api = VirtualRoot.temp.``electron-api.json``
    let Cache = Root.ci.``cache.json``



module Ops =
    /// Clean directories from build material, and temporary files downloaded such as electron-api.json
    let [<Literal>] clean = "clean"
    /// Clean directories from fable generated files
    let [<Literal>] fableClean = "fable-clean"
    /// List releases from electron
    let [<Literal>] listReleases = "list-releases"
    /// List releases from electron with details
    let [<Literal>] listDetailedReleases = "list-detailed-releases"
    /// Download a specified release
    let [<Literal>] downloadApi = "download-api"
    let [<Literal>] downloadLatest = "download-latest"
    /// Combines list releases and download api interactively
    let [<Literal>] downloadInput = "download-input"
    /// Post download cleanup
    let [<Literal>] postDownload = "post-download-clean"
    /// Generate the Fable.Electron bindings
    let [<Literal>] generate = "generate"
    /// Setup docs via npm i or npm ci
    let [<Literal>] setupDocs = "setup-docs"
    /// Run docs in watch mode
    let [<Literal>] docs = "docs"
    /// Build projects
    let [<Literal>] build = "build"
    /// Pack projects
    let [<Literal>] pack = "pack"
    /// Push to nuget
    let [<Literal>] push = "push"
    /// Generate the API Docs (only to be run in an external repo)
    let [<Literal>] generateApiDocs = "generate-api-docs"
    /// Does setup for tests by downloading deps with npm i or npm ci
    let [<Literal>] setupTest = "setup-test"
    /// Run tests
    let [<Literal>] test = "test"
    /// Do post test cleanup
    let [<Literal>] postTest = "post-test"
    /// Sets git for local commits using github bot client
    let [<Literal>] gitPrelude = "git-prelude"
    /// Restores tools in repo
    let [<Literal>] restore = "restore"
    /// Formats files with fantomas
    let [<Literal>] format = "format"
    /// Cron job for use by CI
    let [<Literal>] cron = "cron"
    let [<Literal>] changelogGen = "changelog-gen"
    let [<Literal>] loadCache = "load-cache"
    let [<Literal>] testGitNet = "test-gitnet"
    module Args =
        let [<Literal>] help = "--help"
        let [<Literal>] detailed = "--detailed"
        let [<Literal>] quick = "--quick"
        let [<Literal>] dry = "--dry-run"
        let [<Literal>] commit = "--commit"
        let [<Literal>] release = "--release"
        let [<Literal>] npmCi = "--npm-ci"
        let [<Literal>] skipTest = "--skip-test"
        let [<Literal>] ciRunner = "--ci-runner"
        let [<Literal>] nugetApi = "--nuget-key"
        let [<Literal>] ghKey = "--gh-key"
        let [<Literal>] target = "--target"
        let [<Literal>] debug = "--debug"
module Commands =
    let [<Literal>] docs = "docs"
    let [<Literal>] test = "test"
    let [<Literal>] generateApiDocs = "generate-api-docs"
    let [<Literal>] generate = "generate"
    let [<Literal>] pack = "pack"
    let [<Literal>] cron = "cron"
    let [<Literal>] publish = "publish"
    let [<Literal>] run = "run"
        

[<Literal>]
let githubUsername = "GitHub Action"

[<Literal>]
let githubEmail = "41898282+github-actions[bot]@users.noreply.github.com"

type Args =
    static let mutable args = None
    static let hasFlag value = args |> Option.exists (DocoptResult.hasFlag value)
    static let getFlag value = args |> Option.bind (DocoptResult.tryGetArgument value)

    static member setArgs argsv =
        args <- (Cli.parser : Docopt).Parse(argsv) |> Some
    
    static member detailed = hasFlag Ops.Args.detailed
    static member quick = hasFlag Ops.Args.quick
    static member dryRun = hasFlag Ops.Args.dry
    static member help = hasFlag Ops.Args.help
    static member commit = hasFlag Ops.Args.commit
    static member release = getFlag Ops.Args.release
    static member npmCi = hasFlag Ops.Args.npmCi
    static member skipTest = hasFlag Ops.Args.skipTest
    static member ciRunner = hasFlag Ops.Args.ciRunner
    static member apiKey = getFlag Ops.Args.nugetApi
    static member target = getFlag Ops.Args.target
    static member gitClientToken = getFlag Ops.Args.ghKey
    static member debug = hasFlag Ops.Args.debug
and Cli =
    static member spec =
        $"""
Usage:
    Build.exe {Commands.docs} [options]
    Build.exe {Commands.generate} [options]
    Build.exe {Commands.generateApiDocs} [options]
    Build.exe {Commands.pack} [options]
    Build.exe {Commands.cron} [options]
    Build.exe {Commands.run} [run] [options]
    Build.exe {Commands.publish} [publish] [options]

Run Options [run]:
    --target <NAME>         The target to run

Publish Targets [publish]:
    --forge                 Pack and publish the Fable.Electron.Forge package
    --remoting              Pack and publish the Fable.Electron.Remoting package

Options [options]:
    -h, --help              Show this help message.
                            Note that the `cron` job should only be performed by the CI runners
    -D, --detailed          When printing release information, show all fields.
    -Q, --quick             Skip setup steps, such as installing dependencies (for local environments).
    --dry-run               Collect actions and print them at the end instead of pushing any changes.
    --npm-ci                `npm install` commands are run using `ci` (clean install) instead. Use this
                            if you are encountering 'module missing' errors for npm dependencies.
    --commit                Any steps that can commit changes during the build/push sequence will do so
                            (for use during CI)
    --no-restore            Perform the action without restores if they would otherwise call for it.
    --changelog-gen         Overwrites the changelog if there have been any version changes for Fable.Electron.
    --release <RELEASE>     Perform the actions for the specific release tag.
    --api-docs              Generate the API-Docs
    --ci-runner             Indicate environment is CI runner - this will setup gitbot etc
    --skip-test             Skip tests
    --format                Run fantomas
    --nuget-key <API-KEY>   The key used in authentication to push packages to NuGet.
    --gh-key <PAT>          Personal access token for GitHub to use instead of the CI runner.
    --debug                 Shows the dependency list for the command and args
"""
    static member parser = Docopt(Cli.spec)
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
