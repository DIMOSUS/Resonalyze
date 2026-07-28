# Third-Party Notices

Resonalyze is distributed under the MIT License. The packages below ship inside
the released application, each under its own license. All of them are
MIT-licensed.

| Package | Version | License | Authors |
|---------|---------|---------|---------|
| MathNet.Numerics | 5.0.0 | MIT | Christoph Rüegg, Marcus Cuda, Jurgen Van Gael |
| NAudio | 2.3.0 | MIT | Mark Heath & Contributors |
| NAudio.Asio | 2.3.0 | MIT | Mark Heath |
| NetSparkleUpdater.UI.WinForms | 3.0.1 | MIT | Deadpikle |
| OxyPlot.Core | 2.2.0 | MIT | OxyPlot |
| OxyPlot.WindowsForms | 2.2.0 | MIT | OxyPlot |
| YamlDotNet | 16.2.1 | MIT | Antoine Aubry |
| PDFsharp-MigraDoc-GDI (incl. PDFsharp, MigraDoc) | 6.2.4 | MIT | PDFsharp Team (empira Software GmbH) |

## Build-only dependency

`Tracy-CSharp` 0.13.1 is referenced **only** by the `Tracy` build configuration,
which exists for profiling (see `AGENTS.md`). Releases are built in `Release`, so
it is not part of any distributed binary and is listed separately because its
licensing is not MIT throughout: the C# bindings are MIT (Tracy, the package
author), while the native `TracyClient` library they bundle is the Tracy profiler
by Bartosz Taudul, under the **BSD 3-Clause** license. Anyone redistributing a
`Tracy`-configuration build has to carry that notice as well.

## MIT License

Every package in the released-application table above, and the `Tracy-CSharp`
bindings themselves, are provided under the MIT License:

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

The full license text and copyright for each package is available in that package's
distribution (in the local NuGet cache under `~/.nuget/packages/<package>/<version>/`)
and on nuget.org.
