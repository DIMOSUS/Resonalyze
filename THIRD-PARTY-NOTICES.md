# Third-Party Notices

Resonalyze is distributed under the MIT License (`License.md`). The release
(the zip and the installer) is a single `Resonalyze.exe` with a `licenses`
folder beside it that holds this file, `License.md`, and one folder per shipped
package that carries license files of its own, with those files copied verbatim
from the package at publish:

- `Microsoft.NETCore.App.Runtime.*` and `Microsoft.WindowsDesktop.App.Runtime.*`:
  the .NET runtime and Windows Forms, which the self-contained exe bundles, with
  the .NET `THIRD-PARTY-NOTICES.TXT`.
- `SkiaSharp*` and `HarfBuzzSharp*`: the native `libSkiaSharp.dll` and
  `libHarfBuzzSharp.dll` the main plot draws with. Their
  `THIRD-PARTY-NOTICES.txt` reproduces in full the licenses of **Skia** (Google,
  BSD 3-Clause), **HarfBuzz** ("Old MIT") and the projects compiled into them,
  among them FreeType, libpng, zlib, libjpeg-turbo, libwebp, expat, ICU and etc1
  (Apache 2.0).
- `Microsoft.Extensions.*` (used by PDFsharp) with the .NET notices.
- `NetSparkleUpdater.Chaos.NaCl`, which also carries the Bouncy Castle notice and
  the public-domain Chaos.NaCl credit.

The packages below state their license by expression only and carry no license
file, so their copyright lines, copied from each project's own license file, are
reproduced here; each is provided under the [MIT License](#mit-license) at the
end of this file.

| Package | Version | Copyright notice |
|---------|---------|------------------|
| MathNet.Numerics | 5.0.0 | Copyright (c) 2002-2022 Math.NET |
| NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms, NAudio.WinMM | 2.3.0 | Copyright 2020 Mark Heath |
| NetSparkleUpdater.SparkleUpdater, NetSparkleUpdater.UI.WinForms | 3.0.1 | Copyright (c) 2024 Deadpikle |
| OxyPlot.Core, OxyPlot.WindowsForms, OxyPlot.SkiaSharp | 2.2.0 | Copyright (c) 2014 OxyPlot contributors |
| PDFsharp-GDI, PDFsharp-MigraDoc-GDI (PDFsharp and MigraDoc) | 6.2.4 | Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany |
| YamlDotNet | 16.2.1 | Copyright (c) 2008, 2009, 2010, 2011, 2012, 2013, 2014 Antoine Aubry and contributors |

The packages with license files of their own, all MIT for their managed code:

| Package | Version | Copyright notice |
|---------|---------|------------------|
| .NET runtime and Windows Forms | as built | Copyright (c) .NET Foundation and Contributors |
| Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions | 8.0.2, 8.0.3 | Copyright (c) .NET Foundation and Contributors |
| SkiaSharp, SkiaSharp.HarfBuzz, HarfBuzzSharp (and their NativeAssets.Win32) | 2.88.8, 7.3.0.2 | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| NetSparkleUpdater.Chaos.NaCl | 0.9.3 | Copyright (c) 2024 Deadpikle |

## Bundled reference data

`dsp/Data/GrasFreeFieldCorrections.csv` is an extract of the free-field and
random-incidence correction table published by **GRAS Sound & Vibration A/S**
([GRAS_Free-field_and_Random_Incidence_Corrections.xlsx](https://www.grasacoustics.com/fileadmin-gras/downloads/GRAS_Free-field_and_Random_Incidence_Corrections.xlsx),
SHA-256 `13ab5f3ccbbf1ab53774496cdcc9765e2d79180a99df50309188a7e502e7620b`). It
carries the measured 0°/30°/60°/90° columns of the microphone families
Resonalyze scales from; the remaining angles and the random-incidence column are
not reproduced. The numbers are GRAS measurement data, used here to estimate the
angular behaviour of a user's own microphone, and are neither modified nor
presented as Resonalyze's own. GRAS is not affiliated with this project.

## Build-only dependency

`Tracy-CSharp` 0.13.1 is referenced **only** by the `Tracy` build configuration,
which exists for profiling (see `AGENTS.md`). Releases are built in `Release`, so
it is not part of any distributed binary and is listed separately because its
licensing is not MIT throughout: the C# bindings are MIT (Tracy, the package
author), while the native `TracyClient` library they bundle is the Tracy profiler
by Bartosz Taudul, under the **BSD 3-Clause** license. Anyone redistributing a
`Tracy`-configuration build has to carry that notice as well.

## MIT License

Every package in the tables above (for the native libraries, their managed
wrappers), and the `Tracy-CSharp` bindings themselves, are provided under the MIT
License, each with its copyright notice listed above:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in the
Software without restriction, including without limitation the rights to use, copy,
modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## BSD 3-Clause License

Applies only to the native `TracyClient` library bundled by `Tracy-CSharp`, and
therefore only to a `Tracy`-configuration build — no released Resonalyze binary
contains it. Copyright (c) 2017-2024, Bartosz Taudul <wolf@nereid.pl>. All rights
reserved.

```
Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software without
   specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
