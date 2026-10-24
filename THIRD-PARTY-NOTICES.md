# Third-party components

YingView includes the following independently licensed components. Its source license does not replace their licenses.

- **.NET and WPF**, by Microsoft and contributors: MIT. [dotnet/runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [dotnet/wpf license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT).
- **Magick.NET 15.0.0**, by Dirk Lemstra and contributors: Apache License 2.0. [Magick.NET license](https://github.com/dlemstra/Magick.NET/blob/main/License.txt).
- **ImageMagick**, bundled by Magick.NET: ImageMagick License. Its native distribution includes image codecs and their notices. [ImageMagick license](https://imagemagick.org/script/license.php), [Magick.Native notices](https://github.com/dlemstra/Magick.Native/tree/main/docs).
- **PS-SFTA association hash algorithm**, by Danyfirex and Dany3j, Copyright (c) 2022 Danysys: MIT. The standalone Windows compatibility implementation includes a C# port of its pure hash function, based on [commit 22a32292e576afc976a1167d92b50741ef523066](https://github.com/DanysysTeam/PS-SFTA/blob/22a32292e576afc976a1167d92b50741ef523066/SFTA.ps1). The MIT notice is retained in `ThirdParty/PS-SFTA.txt`.
- **UserChoiceLatestHash**, Copyright (c) 2026 cssxn: MIT. The newer standalone association compatibility calculation is ported from [commit 662070e5cdea4496bc474f4bdfd81f77d4cabab3](https://github.com/cssxn/UserChoiceLatestHash/tree/662070e5cdea4496bc474f4bdfd81f77d4cabab3). Its MIT notice is retained in `ThirdParty/UserChoiceLatestHash.txt`.

The bundled native dependency's full license inventory is distributed in `ThirdParty/` beside the application. These notices are retained with distributed builds.
