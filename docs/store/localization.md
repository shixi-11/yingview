# Client names and languages

Direct downloads are separate builds of the same product:

| Download | Interface and display name | Windows icon |
| --- | --- | --- |
| `YingView-zh-CN.exe` | 简体中文 · 映窗 | Shared B graphic |
| `YingView-en-US.exe` | English · YingView | Shared B graphic |

The executable embeds its distribution language; renaming a downloaded file does not change the language. Chinese and English downloads use their own product metadata, title, About text and installer shortcut names while sharing the same B graphic. File associations remain user-selected. The optional installer reads `client-language.json` and registers the matching display name without changing the user's default app.

The Store uses one product identity and a multilingual `auto` build. Its manifest references `ms-resource` names, its PRI contains `en-US` and `zh-CN` resources and localized launcher icons, and English is the default fallback. The app follows the corresponding supported system language initially; unsupported locales fall back to English. Store listing text and screenshots should be supplied separately for Chinese and English. This does not require duplicate Store products.

Prepare the universal client before packaging:

```powershell
.\scripts\build.ps1 -ClientLanguage auto
```

Then follow the submission checklist using `dist/YingView-auto`. The packaging script rejects language-fixed downloads as Store input. A genuine reserved identity and Publisher are still required; no Store product or public listing has been created by this preparation.
