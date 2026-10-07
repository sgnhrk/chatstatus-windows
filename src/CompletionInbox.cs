using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ChatStatus {
 public class Completion {
  public string Key; public string Id; public string Title; public DateTime At; public bool Confirmed;
 }
 public class CompletionInbox {
  public List<Completion> Items=new List<Completion>();
  readonly string path; readonly JavaScriptSerializer json=new JavaScriptSerializer(); bool writable=true;
  public CompletionInbox(string file){path=file;if(File.Exists(path)){try{Items=json.Deserialize<List<Completion>>(File.ReadAllText(path));if(Items==null || Items.Any(x=>x==null || String.IsNullOrEmpty(x.Key)))throw new InvalidDataException("完了通知の保存データが不正です。");}catch{writable=false;throw;}}}
  public int Pending {get{return Items.Count(x=>!x.Confirmed);}}
  public List<Completion> Observe(IEnumerable<Completion> events,bool baseline){
   var added=new List<Completion>();if(baseline)return added;
   foreach(var e in events){if(Items.Any(x=>x.Key==e.Key))continue;Items.Add(e);added.Add(e);}
   return added;
  }
  public void Save(){if(!writable)throw new InvalidOperationException("保存データを保護するため保存を停止しています。");string tmp=path+".tmp";File.WriteAllText(tmp,json.Serialize(Items),System.Text.Encoding.UTF8);if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}
  public void Confirm(string key){var changed=Items.Where(x=>!x.Confirmed && (key==null || x.Key==key)).ToList();foreach(var c in changed)c.Confirmed=true;try{Save();}catch{foreach(var c in changed)c.Confirmed=false;throw;}}
 }
}
