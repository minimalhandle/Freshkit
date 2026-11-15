# Icon rendering dependencies

These binaries are embedded into Freshkit.exe by tools/icon-build.ps1 and loaded from resources only when needed. Normal builds do not download packages.

| NuGet package | Version | Framework asset | License |
| --- | --- | --- | --- |
| Svg | 3.4.7 | net462 | MS-PL |
| ExCSS | 4.2.3 | netstandard2.0 | MIT |
| System.Memory | 4.5.5 | net461 | MIT |
| System.Buffers | 4.5.1 | net461 | MIT |
| System.Runtime.CompilerServices.Unsafe | 4.5.3 | net461 | MIT |
| System.Numerics.Vectors | 4.5.0 | net46 | MIT |

Source packages: https://api.nuget.org/v3-flatcontainer/{lowercase-package-id}/{version}/{lowercase-package-id}.{version}.nupkg

SVG source: https://github.com/svg-net/SVG (package commit c53af12d973bffef486c3d6a32c941a0d15cd987).
ExCSS source: https://github.com/TylerBrinks/ExCSS.
License and third-party notices accompany the DLLs and are embedded in the executable too.
App logos remain the property of their respective owners; built-in logo URLs are recorded in src/icons/sources.json.
