using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XenMotion.Core.Models;
using XenMotion.UI.Desktop;
using XenMotion.UI.Rendering;
if (args.Length < 2) throw new ArgumentException("Pass output.json and one or more videos.");
var results = new List<object>();
bool reuse = args.Contains("--reuse");
DesktopSurface? keptSurface = null;
VideoWallpaperRenderer? keptRenderer = null;
foreach (string video in args.Skip(1).Where(a=>a!="--reuse")) for(int attempt=0;attempt<3;attempt++) {
 var bounds = new MonitorBounds(-24000,-24000,1920,1080);
 var surface = keptSurface ?? DesktopSurface.TryCreate(GetDesktopWindow(),bounds) ?? throw new InvalidOperationException("No test surface.");
 var renderer = keptRenderer ?? new VideoWallpaperRenderer(NullLogger<VideoWallpaperRenderer>.Instance);
 var watch = Stopwatch.StartNew();
 if(keptRenderer==null) await renderer.InitializeAsync(surface.Handle,surface.LocalBounds);
 double initializeMs=watch.Elapsed.TotalMilliseconds;
 long initialFrames=renderer.PresentedFrameCount;
 var load=Task.Run(()=>renderer.LoadAsync(Path.GetFullPath(video),new WallpaperSettings { TargetFps=30 }));
 while(renderer.PresentedFrameCount==initialFrames && !load.IsCompleted) await Task.Delay(1);
 await load;
 double firstFrameMs=initializeMs + renderer.FirstFrameLoadMilliseconds;
 double readyMs=watch.Elapsed.TotalMilliseconds;
 long before=renderer.PresentedFrameCount;
 await renderer.PlayAsync();
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
 while(renderer.PresentedFrameCount<=before) await Task.Delay(1,timeout.Token);
 double motionMs=watch.Elapsed.TotalMilliseconds;
 if(renderer.PresentedFrameCount<=0) throw new InvalidOperationException("No frame presented.");
 if(reuse) { keptSurface=surface; keptRenderer=renderer; }
 else { await renderer.DisposeAsync(); surface.Dispose(); }
 results.Add(new { video=Path.GetFileName(video),attempt,initializeMs,firstFrameMs,readyMs,motionMs });
 Console.WriteLine($"{Path.GetFileName(video)}: init {initializeMs:F0}, first frame {firstFrameMs:F0}, ready {readyMs:F0}, motion {motionMs:F0} ms");
}
if(keptRenderer!=null) await keptRenderer.DisposeAsync();
keptSurface?.Dispose();
File.WriteAllText(args[0],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
[DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
