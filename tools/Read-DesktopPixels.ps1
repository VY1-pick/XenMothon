param([int]$Samples = 1)
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class DesktopPixelProbe {
 public delegate bool Callback(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, Callback c, IntPtr p);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder b, int n);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
 [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
 [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetStyle(IntPtr h, int index);
 public struct Point { public int X,Y; }
 static string Class(IntPtr h) { var b=new StringBuilder(256); GetClassName(h,b,256); return b.ToString(); }
 public static void Read() {
   IntPtr surface=IntPtr.Zero;
   EnumWindows((h,p)=>{if(Class(h)=="Progman" || Class(h)=="WorkerW")
     EnumChildWindows(h,(child,q)=>{if(Class(child).StartsWith("XenMotionSurface_")) surface=child; return true;},IntPtr.Zero);
     return true;},IntPtr.Zero);
   Console.WriteLine($"{DateTime.UtcNow:O} Foreground={Class(GetForegroundWindow())}; Surface={surface:X} visible={IsWindowVisible(surface)} parent={GetParent(surface):X} next={GetWindow(surface,2):X} style={GetStyle(surface,-16):X}");
   if(surface==IntPtr.Zero) {
     foreach(int x in new[]{100,960,1800}) foreach(int y in new[]{100,540,900}) Console.WriteLine($"{x},{y}: covering={Class(WindowFromPoint(new Point{X=x,Y=y}))}");
     return;
   }
   IntPtr windowDc=GetDC(surface), screenDc=GetDC(IntPtr.Zero);
   try {
     foreach(var p in new[]{new Point{X=20,Y=20},new Point{X=960,Y=540},new Point{X=1800,Y=900}})
       Console.WriteLine($"{p.X},{p.Y}: window={GetPixel(windowDc,p.X,p.Y):X8}, screen={GetPixel(screenDc,p.X,p.Y):X8}, covering={Class(WindowFromPoint(p))}");
   } finally { ReleaseDC(surface,windowDc); ReleaseDC(IntPtr.Zero,screenDc); }
 }
}
'@
for ($sample = 0; $sample -lt $Samples; $sample++) {
    [DesktopPixelProbe]::Read()
    if ($sample -lt $Samples - 1) { Start-Sleep -Seconds 2 }
}
