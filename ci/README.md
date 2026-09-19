# Build

The build and CI tool for Fable.Electron, written against [Partas.Build](https://github.com/shayanhabibi/Partas.Build).
It uses the `gh` cli to list and download electron release assets, targeting the latest release unless one is
requested with `--release`.

```shell
dotnet run -- --help              # every command
dotnet run -- <command> --help    # the options that command's stages read
dotnet run -- <command> --explain # the resolved stage tree, running nothing
```

| Command | What it runs |
|---|---|
| `build` | restore tools, clean, build the `src/` projects (`-p` to choose, `-c` for the configuration) |
| `test` | install the electron test app and run it headless; `--watch` or `--windowed` instead |
| `generate` | download the requested release's `electron-api.json` and regenerate `Fable.Electron/Program.fs` |
| `pack` / `publish` | pack into `bin/`; `publish` also pushes with `--nuget-key` |
| `format` | fantomas over the source and test projects |
| `status` | the versions and bumps a scheduled run would calculate |
| `fable clean|build|test` | the fable-specific equivalents |
| `releases` / `releases download` | list the electron releases, or download one |
| `docs` | install and serve the docs site |
| `tool` | pack this project as the `fable-electron` dotnet tool |
| `cron` | the scheduled generation (below) |

`--quick` skips installs, cleans and downloads. `--dry-run` on `cron` and `publish` prints every git, nuget and
github action instead of performing it.

## The scheduled run

`cron` downloads the latest release (narrowed by `--only-minor`/`--only-patch` against `ci/cache.json`), generates,
builds and tests, then reads the versions GitNet calculates from the commit history. When any package changed, a
major electron delta or a generation failure lands the commit on `ci/electron/<tag>` and opens a pull request into
`develop`, carrying the failures in its body; any other delta commits, tags, packs, publishes and pushes on the
current branch.

## Organisation

`Build.fsproj` sits at the repository root so `dotnet run` works from there; its source lives in `ci/`. That
location has a cost: the SDK's default item globs walk the whole repository, `node_modules` included, before
the tool starts. Moving the project into its own directory brings `dotnet run` down from about thirteen seconds
to about two, at the price of updating the workflows that call it.
