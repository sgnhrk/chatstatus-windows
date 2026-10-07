using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace ChatStatus {
 public class Work { public string Id=""; public string Title=""; public string State="記録なし"; public DateTime Updated; public bool Running; }
 public class Quota { public double Used; public int Minutes; public long Reset; public DateTime Updated; }
 public class FileState { public long Offset; public Work Work=new Work(); public DateTime LastWrite; }
 public class Snapshot { public List<Work> Work=new List<Work>(); public List<Quota> Quotas=new List<Quota>(); public List<Completion> Completions=new List<Completion>();public string Error=""; }
 public class Reader {
  readonly string home; readonly Dictionary<string,FileState> files=new Dictionary<string,FileState>();
  readonly JavaScriptSerializer json=new JavaScriptSerializer(); List<Quota> quotas=new List<Quota>(); DateTime quotaTime;
  readonly List<Completion> completions=new List<Completion>();
  public Reader(string path) {home=path;json.MaxJsonLength=32*1024*1024;}
  public static Dictionary<string,object> Obj(object o) {return o as Dictionary<string,object> ?? new Dictionary<string,object>();}
  public static object Get(Dictionary<string,object> o,string k) {object v;return o.TryGetValue(k,out v)?v:null;}
  public static string Str(Dictionary<string,object> o,string k) {return Convert.ToString(Get(o,k));}
  public void Line(FileState f,string line) {
   var e=Obj(json.DeserializeObject(line)); var p=Obj(Get(e,"payload")); string type=Str(e,"type"); DateTime stamp;
   if(!DateTime.TryParse(Str(e,"timestamp"),null,System.Globalization.DateTimeStyles.RoundtripKind,out stamp)) return;
   stamp=stamp.ToUniversalTime();
   if(type=="session_meta") {f.Work.Id=Str(p,"id"); f.Work.Title=Path.GetFileName(Str(p,"cwd"));}
   if(type!="event_msg")return;
   string ev=Str(p,"type");
   if(ev=="task_started" || ev=="turn_started") {f.Work.Running=true;f.Work.State="実行中（ローカル記録）";f.Work.Updated=stamp;}
   if(ev=="task_complete" || ev=="turn_complete" || ev=="turn_aborted" || ev=="task_aborted") {f.Work.Running=false;f.Work.State=ev.Contains("aborted")?"中断":"完了";f.Work.Updated=stamp;}
   if(ev=="task_complete" || ev=="turn_complete"){string turn=Str(p,"turn_id");completions.Add(new Completion {Id=f.Work.Id,Title=f.Work.Title,At=stamp,Key=f.Work.Id+":"+(String.IsNullOrEmpty(turn)?stamp.ToString("o"):turn)});}
   if(f.Work.Running && stamp>f.Work.Updated)f.Work.Updated=stamp;
   if(ev=="token_count" && stamp>=quotaTime) {
    var rate=Obj(Get(p,"rate_limits"));var found=new List<Quota>();
    foreach(string key in new[]{"primary","secondary"}) {var w=Obj(Get(rate,key));if(w.ContainsKey("used_percent"))found.Add(new Quota {Used=Convert.ToDouble(w["used_percent"]),Minutes=Convert.ToInt32(Get(w,"window_minutes")??0),Reset=Convert.ToInt64(Get(w,"resets_at")??0),Updated=stamp});}
    if(found.Count>0){quotas=found;quotaTime=stamp;}
   }
  }
  public Snapshot Scan() {
   var s=new Snapshot();completions.Clear();var names=new Dictionary<string,string>();
   try {
    string index=Path.Combine(home,"session_index.jsonl");
    if(File.Exists(index))using(var stream=new FileStream(index,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(stream)) { string l;while((l=r.ReadLine())!=null){try {var o=Obj(json.DeserializeObject(l));names[Str(o,"id")]=Str(o,"thread_name");}catch{}} }
    string root=Path.Combine(home,"sessions");
    if(!Directory.Exists(root)){s.Error="Codex のローカル記録がありません";return s;}
    // Read recent directory partitions instead of traversing the full history every tick.
    var candidates=new List<FileInfo>();
    for(int day=0;day<8;day++){string dir=Path.Combine(root,DateTime.Today.AddDays(-day).ToString("yyyy/MM/dd").Replace('/',Path.DirectorySeparatorChar));if(Directory.Exists(dir))candidates.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl"));}
    foreach(var info in candidates.OrderByDescending(x=>x.LastWriteTimeUtc).Take(30)) {
     FileState f;if(!files.TryGetValue(info.FullName,out f)){f=new FileState();files[info.FullName]=f;}
     if(info.Length<f.Offset){f=new FileState();files[info.FullName]=f;}
     if(info.Length>f.Offset)using(var stream=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
      stream.Seek(f.Offset,SeekOrigin.Begin);long end=stream.Length;
      // Consume only complete lines. A writer can be in the middle of a JSON record.
      var bytes=new List<byte>();byte[] buffer=new byte[65536];int n;long pos=f.Offset;
      while(pos<end && (n=stream.Read(buffer,0,(int)Math.Min(buffer.Length,end-pos)))>0){for(int i=0;i<n;i++){pos++;if(buffer[i]==10){try{Line(f,System.Text.Encoding.UTF8.GetString(bytes.ToArray()));}catch{}bytes.Clear();f.Offset=pos;}else bytes.Add(buffer[i]);}}
     }
     f.LastWrite=info.LastWriteTimeUtc;
     string title;if(names.TryGetValue(f.Work.Id,out title))f.Work.Title=title;
    }
    s.Work=files.Values.Where(x=>x.Work.Updated>DateTime.UtcNow.AddDays(-7)).Select(x=>new Work {Id=x.Work.Id,Title=x.Work.Title,Running=x.Work.Running,Updated=x.Work.Updated,State=x.Work.Running && DateTime.UtcNow-x.Work.Updated>TimeSpan.FromMinutes(10)?"状態不明（10分以上更新なし）":x.Work.State}).OrderByDescending(x=>x.Updated).Take(12).ToList();
    s.Quotas=quotas.ToList();
    foreach(var c in completions){string title;if(names.TryGetValue(c.Id,out title))c.Title=title;s.Completions.Add(c);}
   }catch(Exception ex){s.Error="記録の読込に失敗: "+ex.Message;}
   return s;
  }
 }
 public class App:ApplicationContext {
  readonly NotifyIcon tray=new NotifyIcon();readonly Form panel=new Form();readonly TaskbarStrip mini;
  readonly Label summary=new Label();readonly Label quota=new Label();readonly ListView works=new ListView();readonly ListView completionsView=new ListView();
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly Reader reader;
  readonly string data;CompletionInbox inbox;Snapshot current=new Snapshot();bool busy,quitting,first=true;Icon owned;
  readonly JavaScriptSerializer json=new JavaScriptSerializer();
  public App(bool preview) {
   data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");Directory.CreateDirectory(data);
   string home=Environment.GetEnvironmentVariable("CODEX_HOME");if(String.IsNullOrEmpty(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");reader=new Reader(home);
   try{inbox=new CompletionInbox(Path.Combine(data,"completions.json"));}catch(Exception ex){MessageBox.Show("完了通知を読み込めません。保存ファイルを保持して終了します。\n"+ex.Message,"ChatStatus");throw;}
   BuildPanel();mini=new TaskbarStrip(data);mini.OpenDetails=ShowPanel;var handle=panel.Handle;
   tray.Text="ChatStatus — Codex";tray.Icon=SystemIcons.Application;tray.Visible=true;
   var menu=new ContextMenuStrip();menu.Items.Add("状況を開く",null,(a,b)=>ShowPanel());menu.Items.Add("表示位置をリセット",null,(a,b)=>mini.ResetPosition());menu.Items.Add("完了通知をすべて確認済みに",null,(a,b)=>ConfirmCompletion(true));menu.Items.Add("今すぐ更新",null,(a,b)=>Refresh());menu.Items.Add("終了",null,(a,b)=>Exit());tray.ContextMenuStrip=menu;
   tray.MouseClick+=(a,b)=>{if(b.Button==MouseButtons.Left)ShowPanel();};
   tray.BalloonTipClicked+=(a,b)=>ShowPanel();
   timer.Interval=5000;timer.Tick+=(a,b)=>Refresh();if(!preview){timer.Start();Refresh();}
   if(preview){timer.Stop();current=reader.Scan();Render();panel.Show();panel.Refresh();using(var bmp=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));}using(var bmp=new Bitmap(mini.Width,mini.Height)){mini.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.MakeTransparent(Color.Magenta);bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"taskbar-preview.png"));}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"taskbar-layout.txt"),"Strip bounds: "+mini.Bounds+"; Visible: "+mini.Visible);Exit();}
  }
  void BuildPanel() {
   panel.Text="ChatStatus | Codex";panel.ClientSize=new Size(620,720);panel.MinimumSize=new Size(640,760);panel.Font=new Font("Yu Gothic UI",10);panel.BackColor=Color.FromArgb(24,29,38);panel.ForeColor=Color.FromArgb(228,235,244);panel.StartPosition=FormStartPosition.CenterScreen;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(20)};
   foreach(int height in new[]{42,50,110,36,155,36,140,42,40})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));panel.Controls.Add(layout);
   layout.Controls.Add(new Label{Text="ChatStatus   /   常駐ダッシュボード",Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",17,FontStyle.Bold)},0,0);
   layout.Controls.Add(new Label{Text="Codex の作業完了を通知します。\n未確認の通知は、確認済みにするまで件数に残ります。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(181,193,211)},0,1);
   quota.Dock=DockStyle.Fill;quota.Text="Codex 使用量を読込中…";layout.Controls.Add(quota,0,2);
   summary.Dock=DockStyle.Fill;layout.Controls.Add(summary,0,3);
   SetupList(works,new[]{"Codex の作業","状態","記録時刻"},new[]{275,185,100});layout.Controls.Add(works,0,4);
   layout.Controls.Add(new Label{Text="完了・未確認（Codex）",Dock=DockStyle.Fill},0,5);
   SetupList(completionsView,new[]{"完了した作業","完了時刻","状態"},new[]{280,165,110});layout.Controls.Add(completionsView,0,6);
   var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.Add(Button("選択を確認済みに",()=>ConfirmCompletion(false)));actions.Controls.Add(Button("すべて確認済みに",()=>ConfirmCompletion(true)));actions.Controls.Add(Button("表示位置リセット",()=>mini.ResetPosition()));layout.Controls.Add(actions,0,7);
   var links=new FlowLayoutPanel{Dock=DockStyle.Fill};links.Controls.Add(Button("更新",Refresh));links.Controls.Add(Button("トレイへ閉じる",()=>panel.Hide()));layout.Controls.Add(links,0,8);
   panel.FormClosing+=(a,b)=>{if(!quitting){b.Cancel=true;panel.Hide();}};
  }
  void SetupList(ListView view,string[] cols,int[] widths){view.Dock=DockStyle.Fill;view.View=View.Details;view.FullRowSelect=true;view.MultiSelect=false;view.HideSelection=false;view.BackColor=Color.FromArgb(32,39,51);view.ForeColor=Color.White;view.BorderStyle=BorderStyle.None;for(int i=0;i<cols.Length;i++)view.Columns.Add(cols[i],widths[i]);}
  Button Button(string text,Action click){var b=new Button{Text=text,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(46,58,76),ForeColor=Color.White};b.Click+=(a,e)=>click();return b;}
  void ShowPanel(){panel.Show();panel.WindowState=FormWindowState.Normal;panel.Activate();}
  void Refresh(){if(busy || quitting)return;busy=true;Task.Run(()=>reader.Scan()).ContinueWith(t=>{if(quitting)return;try{panel.BeginInvoke((Action)(()=>{busy=false;if(t.IsFaulted){summary.Text="更新に失敗しました";return;}current=t.Result;Render();}));}catch{busy=false;}});}
  void Render(){
   int running=current.Work.Count(x=>x.Running && !x.State.StartsWith("状態不明"));summary.Text=String.IsNullOrEmpty(current.Error)?"Codex：実行中 "+running+" 件  /  直近のローカル記録（5秒間隔）":current.Error;
   quota.Text="Codex 使用量（最後に記録された値）\n"+(current.Quotas.Count==0?"取得できる使用量の記録がありません":String.Join("\n",current.Quotas.Select(q=>{DateTime reset=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(q.Reset).ToLocalTime();string window=q.Minutes>=1440?(q.Minutes/1440)+"日枠":q.Minutes+"分枠";return window+"：使用 "+q.Used.ToString("0.#")+"%  / 残り "+Math.Max(0,100-q.Used).ToString("0.#")+"%   リセット "+reset.ToString("MM/dd HH:mm")+(reset<DateTime.Now?"（期限経過・再取得待ち）":"")+"\n記録 "+q.Updated.ToLocalTime().ToString("MM/dd HH:mm:ss");})));
   works.BeginUpdate();works.Items.Clear();foreach(var w in current.Work){var item=new ListViewItem(new[]{String.IsNullOrEmpty(w.Title)?w.Id:w.Title,w.State,w.Updated.ToLocalTime().ToString("MM/dd HH:mm")});works.Items.Add(item);}works.EndUpdate();
   var added=inbox.Observe(current.Completions,first);if(String.IsNullOrEmpty(current.Error))first=false;if(added.Count>0){try{inbox.Save();}catch(Exception ex){MessageBox.Show("完了通知の保存に失敗しました: "+ex.Message);}Notify("Codex の作業が完了（"+added.Count+"件）",String.Join(" / ",added.Select(x=>x.Title)));}RenderCompletions();
   UpdateStatus();UpdateIcon(running);
  }
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
  void UpdateIcon(int running){using(var bmp=new Bitmap(32,32))using(var g=Graphics.FromImage(bmp)){g.Clear(Color.Transparent);using(var brush=new SolidBrush(running>0?Color.FromArgb(45,190,145):Color.FromArgb(90,145,224)))g.FillEllipse(brush,1,1,30,30);using(var font=new Font("Segoe UI",15,FontStyle.Bold))g.DrawString(running>0?running.ToString():"C",font,Brushes.White,new RectangleF(0,1,32,30),new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});IntPtr h=bmp.GetHicon();Icon next=(Icon)Icon.FromHandle(h).Clone();DestroyIcon(h);tray.Icon=next;if(owned!=null)owned.Dispose();owned=next;}}
  void RenderCompletions(){string selected=completionsView.SelectedItems.Count>0?Convert.ToString(completionsView.SelectedItems[0].Tag):"";completionsView.Items.Clear();foreach(var c in inbox.Items.Where(x=>!x.Confirmed).OrderByDescending(x=>x.At)){var item=new ListViewItem(new[]{String.IsNullOrEmpty(c.Title)?c.Id:c.Title,c.At.ToLocalTime().ToString("MM/dd HH:mm:ss"),"未確認"});item.Tag=c.Key;completionsView.Items.Add(item);item.Selected=c.Key==selected;}}
  void ConfirmCompletion(bool all){string key=null;if(!all){if(completionsView.SelectedItems.Count==0)return;key=Convert.ToString(completionsView.SelectedItems[0].Tag);}try{inbox.Confirm(key);}catch(Exception ex){MessageBox.Show("確認状態を保存できませんでした: "+ex.Message);}RenderCompletions();UpdateStatus();}
  void UpdateStatus(){int running=current.Work.Count(x=>x.Running && !x.State.StartsWith("状態不明"));mini.SetValues(running,current.Quotas.Count>0?current.Quotas[0].Used.ToString("0.#")+"%"+(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(current.Quotas[0].Reset)<DateTime.UtcNow?" 期限切":""):"未取得",inbox.Pending);tray.Text="ChatStatus | Codex 実行中 "+running+" | 完了・未確認 "+inbox.Pending;}
  void Notify(string title,string text){tray.ShowBalloonTip(10000,title,text,ToolTipIcon.Info);}
  void Exit(){quitting=true;timer.Stop();tray.Visible=false;tray.Dispose();if(owned!=null)owned.Dispose();panel.Dispose();mini.Dispose();ExitThread();}
 }
 public static class Program {
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [STAThread] public static void Main(string[] args){
   SetProcessDPIAware();
   if(args.Length>0 && args[0]=="--test"){try{Tests();}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"FAIL: "+ex.GetType().Name+": "+ex.Message+"\n"+ex.StackTrace);Environment.ExitCode=1;}return;}
   bool created;using(var mutex=new Mutex(true,"Local\\ChatStatusTray-v1",out created)){if(!created)return;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool preview=args.Contains("--preview");var app=new App(preview);if(!preview)Application.Run(app);}
  }
  static void Tests(){
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work",Guid.NewGuid().ToString());Directory.CreateDirectory(root);
   string dir=Path.Combine(root,"sessions",DateTime.Today.ToString("yyyy/MM/dd").Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(dir);
   string path=Path.Combine(dir,"test.jsonl");string ts=DateTime.UtcNow.ToString("o");
   string start="{\"timestamp\":\""+ts+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n";
   File.WriteAllText(path,start);var reader=new Reader(root);if(!reader.Scan().Work.Single().Running)throw new Exception("start");
   string complete="{\"timestamp\":\""+ts+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\"}}";
   File.AppendAllText(path,complete);if(!reader.Scan().Work.Single().Running)throw new Exception("partial line");
   File.AppendAllText(path,"\n");var finished=reader.Scan();if(finished.Work.Single().Running || finished.Completions.Count!=1)throw new Exception("completion event");
   if(reader.Scan().Completions.Count!=0)throw new Exception("incremental duplicate");
   File.AppendAllText(path,"{\"timestamp\":\""+ts+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":22,\"window_minutes\":10080,\"resets_at\":1791974515}}}}\n");
   if(reader.Scan().Quotas.Single().Used!=22)throw new Exception("quota");
   File.AppendAllText(path,"invalid\n");if(reader.Scan().Quotas.Count!=1)throw new Exception("malformed");
   string store=Path.Combine(root,"completions.json");var inbox=new CompletionInbox(store);
   if(inbox.Observe(finished.Completions,true).Count!=0 || inbox.Pending!=0)throw new Exception("historical baseline");
   if(inbox.Observe(finished.Completions,false).Count!=1 || inbox.Pending!=1)throw new Exception("pending count");
   if(inbox.Observe(finished.Completions,false).Count!=0 || inbox.Pending!=1)throw new Exception("deduplicate");
   inbox.Save();inbox=new CompletionInbox(store);if(inbox.Pending!=1)throw new Exception("restart persistence");
   inbox.Confirm(finished.Completions[0].Key);if(inbox.Pending!=0)throw new Exception("acknowledge");
   inbox=new CompletionInbox(store);inbox.Observe(finished.Completions,false);if(inbox.Pending!=0)throw new Exception("acknowledged duplicate");
   File.AppendAllText(path,complete.Replace("turn-1","turn-2")+"\n"+complete.Replace("turn-1","turn-3")+"\n"+start);
   var fast=reader.Scan();if(fast.Work.Single().Running!=true || fast.Completions.Count!=2)throw new Exception("multiple completions before next start");
   inbox.Observe(fast.Completions,false);if(inbox.Pending!=2)throw new Exception("multiple pending");inbox.Confirm(null);if(new CompletionInbox(store).Pending!=0)throw new Exception("acknowledge all");
   File.AppendAllText(path,complete.Replace("task_complete","turn_aborted")+"\n");if(reader.Scan().Completions.Count!=0)throw new Exception("abort not completion");
   File.WriteAllText(store,"invalid");bool rejected=false;try{new CompletionInbox(store);}catch{rejected=true;}if(!rejected || File.ReadAllText(store)!="invalid")throw new Exception("corruption protection");
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"PASS: start, completion events, partial JSON, incremental read, quota, malformed JSON, historical baseline, deduplication, pending count, restart persistence, acknowledgment, multiple completions, abort, corruption protection\n");
  }
 }
}
