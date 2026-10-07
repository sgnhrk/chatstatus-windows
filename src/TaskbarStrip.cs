using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ChatStatus {
 public class TaskbarStrip:Form {
  [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
  [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
  readonly System.Windows.Forms.Timer anchorTimer=new System.Windows.Forms.Timer();
  readonly string positionFile;float ratio=0.61f; bool dragging;Point start;int origin;Rectangle taskbar;float scale=1;bool light=true;
  string[] headings={"CDX 実行","使用 / 記録値","リマインド","ChatGPT"};
  string[] values={"…","…","0 件","未連携"};
  public Action OpenDetails;
  public TaskbarStrip(string data) {
   positionFile=Path.Combine(data,"taskbar-position.txt");
   try{float saved;if(File.Exists(positionFile)&&float.TryParse(File.ReadAllText(positionFile),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out saved))ratio=Math.Max(0,Math.Min(1,saved));}catch{}
   AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
   BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;Cursor=Cursors.SizeAll;
   var menu=new ContextMenuStrip();menu.Items.Add("詳細を開く",null,(s,e)=>{if(OpenDetails!=null)OpenDetails();});menu.Items.Add("位置をリセット",null,(s,e)=>ResetPosition());ContextMenuStrip=menu;
   MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){dragging=true;start=Cursor.Position;origin=Left;Capture=true;}};
   MouseMove+=(s,e)=>{if(dragging){int x=Math.Max(taskbar.Left,Math.Min(taskbar.Right-Width,origin+Cursor.Position.X-start.X));Left=x;ratio=taskbar.Width>Width?(float)(x-taskbar.Left)/(taskbar.Width-Width):0;}};
   MouseUp+=(s,e)=>{if(dragging){dragging=false;Capture=false;SavePosition();if(Math.Abs(Cursor.Position.X-start.X)<4 && OpenDetails!=null)OpenDetails();}};
   anchorTimer.Interval=1000;anchorTimer.Tick+=(s,e)=>AnchorToTaskbar();anchorTimer.Start();AnchorToTaskbar();
  }
  protected override bool ShowWithoutActivation {get{return true;}}
  protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080;return p;}}
  void SavePosition(){try{File.WriteAllText(positionFile,ratio.ToString(System.Globalization.CultureInfo.InvariantCulture));}catch{}}
  public void ResetPosition(){ratio=0.61f;SavePosition();AnchorToTaskbar();}
  public void SetValues(int running,string usage,int pending,int due){values=new[]{running+" 件",usage,pending+" 件"+(due>0?" / !"+due:""),"未連携"};Invalidate();}
  public void AnchorToTaskbar(){
   if(dragging)return;IntPtr h=FindWindow("Shell_TrayWnd",null);Rect r;
   if(h==IntPtr.Zero || !GetWindowRect(h,out r) || !IsWindowVisible(h)){Hide();return;}
   taskbar=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
   int state;if(SHQueryUserNotificationState(out state)==0 && (state==2 || state==3)){Hide();return;}
   // Auto-hidden bars must not leave a floating strip on the desktop.
   Rectangle screen=Screen.FromHandle(h).Bounds;bool horizontal=taskbar.Width>=taskbar.Height;
   if(horizontal && (taskbar.Top>=screen.Bottom-3 || taskbar.Bottom<=screen.Top+3)){Hide();return;}
   try{scale=Math.Max(1,GetDpiForWindow(h)/96f);}catch{scale=1;}
   try{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))light=key==null || Convert.ToInt32(key.GetValue("SystemUsesLightTheme",1))!=0;}catch{}
   int width=horizontal?(int)(360*scale):Math.Max(1,taskbar.Width-4);
   int height=horizontal?Math.Min((int)(48*scale),Math.Max(1,taskbar.Height-4)):(int)(160*scale);
   int x=horizontal?taskbar.Left+(int)((taskbar.Width-width)*ratio):taskbar.Left+2;
   int y=horizontal?taskbar.Top+(taskbar.Height-height)/2:taskbar.Top+(int)((taskbar.Height-height)*ratio);
   SetWindowPos(Handle,new IntPtr(-1),x,y,width,height,0x0010|0x0040);if(!Visible)Show();Invalidate();
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;bool horizontal=Width>Height;
   using(var headFont=new Font("Yu Gothic UI",10*scale,FontStyle.Bold,GraphicsUnit.Pixel))using(var valFont=new Font("Segoe UI",14*scale,FontStyle.Bold,GraphicsUnit.Pixel))using(var accent=new SolidBrush(light?Color.FromArgb(0,126,171):Color.FromArgb(83,206,244)))using(var ink=new SolidBrush(light?Color.FromArgb(25,45,60):Color.FromArgb(236,243,250)))using(var separator=new Pen(Color.FromArgb(140,155,165))) {
    for(int i=0;i<4;i++){float cell=horizontal?Width/4f:Height/4f;float x=horizontal?i*cell+6*scale:5*scale;float y=horizontal?2*scale:i*cell+2*scale;
     g.DrawString(headings[i],headFont,accent,x,y);g.DrawString(values[i],valFont,ink,x,y+15*scale);
     if(i>0 && horizontal)g.DrawLine(separator,i*cell,5*scale,i*cell,Height-5*scale);
    }
   }
  }
  protected override void Dispose(bool disposing){if(disposing)anchorTimer.Dispose();base.Dispose(disposing);}
 }
}
