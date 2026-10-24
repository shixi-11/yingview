using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YingView.Updates;

namespace YingView.AppTests;

internal static class UpdateAssertions
{
    internal static async Task Run(string output)
    {
        var bytes = Encoding.UTF8.GetBytes("isolated update download fixture");
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        object Asset(string name, string? hash = null) => new { name, digest = hash ?? digest, browser_download_url = $"https://github.com/shixi-11/yingview/releases/download/v0.3.14/{name}" };
        object Release(string tag, object[] assets, bool draft = false, bool prerelease = false) => new
        { tag_name = tag, name = tag, html_url = $"https://github.com/shixi-11/yingview/releases/tag/{tag}", draft, prerelease, assets };
        var releases = JsonSerializer.Serialize(new[] {
            Release("v0.3.14", [Asset("YingView-zh-CN.exe"),Asset("YingView-en-US.exe")]),
            Release("v9.0.0-beta", [Asset("YingView-zh-CN.exe")]),
            Release("v9.0.1", [Asset("YingView-zh-CN.exe")], draft:true),
            Release("v9.0.2", [Asset("YingView-zh-CN.exe")], prerelease:true),
            Release("v9.0.3", [Asset("YingView-macOS-en-US.dmg")]) });
        HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
        var calls = new List<Uri>();
        using var service = new UpdateService(new Handler(request => {
            calls.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "api.github.com") return Json(releases);
            if (request.RequestUri.Host == "github.com") {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/file.exe?token=test"); return redirect;
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }), downloadDirectory:Path.Combine(output,"updates","good"));
        var result = await service.CheckAsync(new Version(0,3,13),"zh");
        Require(result.Status==UpdateCheckStatus.UpdateAvailable && result.Version==new Version(0,3,14) && result.AssetName=="YingView-zh-CN.exe", "Only a stable published release for the matching Windows language may be offered.");
        Require((await service.CheckAsync(new Version(0,3,15),"en")).Status==UpdateCheckStatus.UpToDate,"Updates must not downgrade the viewer.");
        var count=calls.Count;
        Require((await service.CheckAsync(new Version(0,3,13),"zh",storeManaged:true)).Status==UpdateCheckStatus.StoreManaged && calls.Count==count,"Store clients must not use standalone updates.");
        var percentages=new List<double>();
        var downloaded=await service.DownloadVerifiedAsync(result,progress:new InlineProgress(value=>percentages.Add(value)));
        Require(File.ReadAllBytes(downloaded.FilePath).SequenceEqual(bytes) && downloaded.Sha256==digest[7..] && calls.Any(c=>c.Host=="release-assets.githubusercontent.com"),"A GitHub CDN download must be verified, closed and finalized successfully.");
        Require(percentages.Count>0 && percentages[^1]==1 && percentages.All(p=>p is >=0 and <=1),"Download progress must reach 100% and stay within its bounds.");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();
            try {await service.DownloadVerifiedAsync(result,canceled.Token);throw new Exception("Canceled downloads must stop.");}
            catch(OperationCanceledException) { }
        }
        Require((await service.CheckAsync(new Version(0,3,13),"en")).AssetName=="YingView-en-US.exe","English clients must not receive Chinese builds.");
        using var mismatch=new UpdateService(new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])}), downloadDirectory:Path.Combine(output,"updates","mismatch"));
        await Reject(()=>mismatch.DownloadVerifiedAsync(result),"A bad digest must stop installation.");
        Require(!Directory.EnumerateFiles(Path.Combine(output,"updates","mismatch")).Any(),"Failed downloads must not remain installable.");
        using var external=new UpdateService(new Handler(_=>{
            var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new Uri("https://example.com/YingView-zh-CN.exe");return response;
        }), downloadDirectory:Path.Combine(output,"updates","external"));
        await Reject(()=>external.DownloadVerifiedAsync(result),"An external redirect must be rejected.");
        using var invalid=new UpdateService(new Handler(_=>Json(JsonSerializer.Serialize(new[]{Release("v0.3.14",[Asset("YingView-zh-CN.exe","sha256:bad")])}))));
        Require((await invalid.CheckAsync(new Version(0,3,13),"zh")).Status==UpdateCheckStatus.Failed,"Missing valid release digest must disable installation.");
        using var none=new UpdateService(new Handler(_=>Json("[]")));
        Require((await none.CheckAsync(new Version(0,3,13),"zh")).Status==UpdateCheckStatus.NoWindowsRelease,"Missing Windows releases must be reported honestly.");
        Require(!UpdateService.TryParseStrictVersion("v0.3.14-beta",out _) && !UpdateService.IsTrustedAssetUrl("https://github.com/other/yingview/releases/download/v1.0.0/YingView-zh-CN.exe","YingView-zh-CN.exe"),"Unstable tags and unrelated repositories must be rejected.");
        var stage=Path.Combine(output,"updates","installation-resources");UpdateInstaller.PrepareInstallation(stage,typeof(App).Assembly.Location);
        foreach(var file in new[]{"LICENSE","NOTICE","CLIENT-LICENSE.txt","CLIENT-LICENSE.zh-CN.txt","LICENSING.md","THIRD-PARTY-NOTICES.md","Install.ps1","ShortcutIdentity.cs","client-language.json"})
            Require(File.Exists(Path.Combine(stage,file)) && new FileInfo(Path.Combine(stage,file)).Length>0,"Installation must include its own resources: "+file);
        Console.WriteLine("Update matching, CDN download, digest rejection, downgrade prevention and installation payload checks passed.");
    }
    private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    private sealed class InlineProgress(Action<double> report):IProgress<double> {public void Report(double value)=>report(value);}
    private static async Task Reject(Func<Task> action,string message) {try{await action();}catch{return;}throw new InvalidOperationException(message);}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();var response=respond(request);response.RequestMessage=request;return Task.FromResult(response);
        }
    }
}
