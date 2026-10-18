using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Win32;

[assembly: AssemblyTitle("AI工具管理器")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
namespace AiToolManager {
 public class ToolItem {
  public string Id{get;set;} public string Name{get;set;} public string Title{get;set;} public string Category{get;set;} public string Status{get;set;} public string Note{get;set;} public string Client{get;set;} public string Path{get;set;} public string Evidence{get;set;} public string Modified{get;set;} public string Description{get;set;} public string[] Usage{get;set;} public string DocPath{get;set;} public string DocSource{get;set;} public string RepositoryUrl{get;set;}
 }
 public class RootInfo {public string Path{get;set;} public string Label{get;set;} public int Depth{get;set;}}
 public class Report {public List<ToolItem> Items{get;set;} public List<RootInfo> Roots{get;set;} public string[] Warnings{get;set;} public int Directories{get;set;} public bool Cancelled{get;set;} public string ScannedAt{get;set;}}
 public class Settings {public List<string> ExtraRoots{get;set;} public bool AutoUpdate{get;set;} public Settings(){ExtraRoots=new List<string>();AutoUpdate=true;}}
 public class Change {public string Title;public string Note;}
 public class Program {
  [STAThread] public static int Main(string[] args){try{var ui=new Manager(args);ui.Window.ShowDialog();return ui.ExitCode;}catch(Exception ex){var p=Arg(args,"--test-output");if(p!=null)File.WriteAllText(p,ex.ToString());else MessageBox.Show("启动失败："+ex.Message,"AI 工具管理器");return 1;}}
  public static string Arg(string[] a,string key){int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
 }
 public class Manager {
  public Window Window; public int ExitCode=0;
  readonly string home,folder,settingsFile; readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=20000000};
  readonly string[] cats={"skills","mcp","plugins","projects","apps","downloads"};
  readonly string[] labels={"Skills","MCP 服务","插件","GitHub / 源码","独立软件","下载文件"};
  readonly string[] subtitles={"技能文件","服务配置","客户端扩展","本地项目","应用登记","安装包与归档"};
  readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
  readonly List<Change> changes=new List<Change>(); readonly DispatcherTimer clock=new DispatcherTimer();
  readonly List<string> checks=new List<string>(); readonly string[] args;
  List<ToolItem> items=new List<ToolItem>(); List<ToolItem> filtered=new List<ToolItem>(); Settings settings; Report report; ToolItem selected;
  bool busy,closing,rendering,showChanges,hasSnapshot,detail,locationTab; int pageIndex=0;const int PageSize=30;string category="all";long dirtyAt;DateTime lastScan=DateTime.MinValue;Hashtable control;
  string testHome,testOutput,capture;bool selfTest;int testStage;DateTime testStarted;bool testEnded;
  T U<T>(string n) where T:class{return Window.FindName(n) as T;}
  static Brush B(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
  static TextBlock Text(string value,double size,string color){return new TextBlock{Text=value??"",FontSize=size,Foreground=B(color),TextWrapping=TextWrapping.Wrap};}
  static Button Btn(string label){return new Button{Content=label};}
  string CatName(string id){int i=Array.IndexOf(cats,id);return i>=0?labels[i]:id;}
  string CatIcon(string id){int i=Array.IndexOf(cats,id);return i>=0?new[]{"S","M","P","G","AI","↓"}[i]:"AI";}
  public Manager(string[] arguments){
   args=arguments;folder=AppDomain.CurrentDomain.BaseDirectory;testHome=Program.Arg(args,"--test-home");testOutput=Program.Arg(args,"--test-output");capture=Program.Arg(args,"--capture");selfTest=args.Contains("--self-test");
   if(selfTest&&(testHome==null||!File.Exists(System.IO.Path.Combine(testHome,".ai-manager-test"))))throw new Exception("自检必须使用带测试标记的隔离目录。");
   home=testHome??Environment.GetEnvironmentVariable("USERPROFILE")??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);settingsFile=System.IO.Path.Combine(folder,selfTest?"TestData/settings.json":"Data/settings.json");
   settings=new Settings();try{if(File.Exists(settingsFile))settings=json.Deserialize<Settings>(File.ReadAllText(settingsFile));}catch{settings=new Settings();}
   if(settings.ExtraRoots==null)settings.ExtraRoots=new List<string>();if(selfTest){settings=new Settings();settings.ExtraRoots.Add(System.IO.Path.Combine(home,"tools"));}
   Window=(Window)XamlReader.Parse(Resource("MainWindow.xaml"));
   try{using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")){Window.Icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);}}catch{}
   U<Button>("LibraryNav").Click+=(s,e)=>{showChanges=false;detail=false;Render();};
   U<Button>("ChangesNav").Click+=(s,e)=>{showChanges=true;detail=false;Render();};
   U<Button>("Refresh").Click+=(s,e)=>StartScan("手动刷新");
   U<Button>("Cancel").Click+=(s,e)=>{if(control!=null)control["Cancel"]=true;Notice("正在取消；将保留上次完整结果。",false);};
   U<Button>("Manage").Click+=(s,e)=>ManageRoots();
   U<Button>("Back").Click+=(s,e)=>{detail=false;Render();};
   U<Button>("GuideTab").Click+=(s,e)=>{locationTab=false;RenderDetail();};
   U<Button>("LocationTab").Click+=(s,e)=>{locationTab=true;RenderDetail();};
   U<Button>("ReadDoc").Click+=(s,e)=>ReadDocumentation();
   U<Button>("OpenRepo").Click+=(s,e)=>{try{Uri u;if(Uri.TryCreate(selected.RepositoryUrl,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="github.com")Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){Info(ex.Message);}};
   U<Button>("Reveal").Click+=(s,e)=>Reveal();U<Button>("CopyPath").Click+=(s,e)=>{try{if(selected!=null)Clipboard.SetText(selected.Path);}catch{Info("剪贴板暂不可用，可直接选择路径文字复制。");}};
   U<Button>("Shortcut").Click+=(s,e)=>CreateShortcut();U<Button>("Warnings").Click+=(s,e)=>ShowWarnings();
   U<TextBox>("Search").TextChanged+=(s,e)=>{if(!rendering){pageIndex=0;RenderCards();}};
   U<ComboBox>("ClientFilter").SelectionChanged+=(s,e)=>{if(!rendering){pageIndex=0;RenderCards();}};
   U<Button>("AllCategories").Click+=(s,e)=>{category="all";pageIndex=0;Render();};
   U<Button>("Prev").Click+=(s,e)=>{if(pageIndex>0){pageIndex--;RenderCards();U<ScrollViewer>("CardScroll").ScrollToTop();}};
   U<Button>("Next").Click+=(s,e)=>{if((pageIndex+1)*PageSize<filtered.Count){pageIndex++;RenderCards();U<ScrollViewer>("CardScroll").ScrollToTop();}};
   U<ScrollViewer>("CardScroll").SizeChanged+=(s,e)=>ResizeCards();
   U<CheckBox>("AutoUpdate").IsChecked=settings.AutoUpdate;
   U<CheckBox>("AutoUpdate").Checked+=(s,e)=>{settings.AutoUpdate=true;SaveSettings();RebuildWatchers();};
   U<CheckBox>("AutoUpdate").Unchecked+=(s,e)=>{settings.AutoUpdate=false;SaveSettings();DisposeWatchers();};
   U<ComboBox>("ClientFilter").Items.Add("所有客户端");U<ComboBox>("ClientFilter").SelectedIndex=0;
   Window.ContentRendered+=(s,e)=>{if(!hasSnapshot&&!busy){testStarted=DateTime.UtcNow;StartScan("首次盘点");}};
   Window.Closing+=(s,e)=>{closing=true;clock.Stop();DisposeWatchers();if(control!=null)control["Cancel"]=true;};
   clock.Interval=TimeSpan.FromMilliseconds(350);clock.Tick+=(s,e)=>Tick();clock.Start();Render();
  }
  static string Resource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new FileNotFoundException(name);using(var r=new StreamReader(s,Encoding.UTF8))return r.ReadToEnd().TrimStart('\uFEFF');}}
  void Info(string text){MessageBox.Show(Window,text,"AI 工具管理器",MessageBoxButton.OK,MessageBoxImage.Information);}
  void Notice(string text,bool warning){U<TextBlock>("Notice").Text=text;U<Border>("NoticeBox").Background=B(warning?"#FBF2E5":"#EAF5F0");U<TextBlock>("Notice").Foreground=B(warning?"#966124":"#25705A");}
  void SaveSettings(){try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsFile));string tmp=settingsFile+".tmp";File.WriteAllText(tmp,json.Serialize(settings),new UTF8Encoding(false));File.Copy(tmp,settingsFile,true);File.Delete(tmp);}catch(Exception ex){Notice("设置未能保存："+ex.Message,true);}}
  void StartScan(string reason){
   if(closing||busy)return;busy=true;U<Button>("Refresh").IsEnabled=false;U<Button>("Manage").IsEnabled=false;U<Button>("Cancel").Visibility=Visibility.Visible;Notice(reason+"：正在只读扫描…",false);
   control=Hashtable.Synchronized(new Hashtable());control["Cancel"]=false;var roots=settings.ExtraRoots.ToArray();var token=control;
   string script="param($HomePath,$Roots,$Control,$SkipApps)\n"+Resource("Metadata.ps1")+"\n"+Resource("Inventory.ps1")+"\nGet-AiInventory -HomePath $HomePath -AdditionalRoots $Roots -Control $Control -SkipInstalledApps:$SkipApps | ConvertTo-Json -Depth 8 -Compress";
   Task.Factory.StartNew(()=>{
    using(var ps=PowerShell.Create()){
     ps.AddScript(script).AddArgument(home).AddArgument(roots).AddArgument(token).AddArgument(selfTest);
     var result=ps.Invoke();if(result.Count==0)throw new Exception("扫描没有返回结果。"+string.Join("; ",ps.Streams.Error.Select(x=>x.ToString())));
     return new JavaScriptSerializer{MaxJsonLength=20000000}.Deserialize<Report>(result[result.Count-1].ToString());
    }
   }).ContinueWith(t=>{if(closing)return;Window.Dispatcher.BeginInvoke(new Action(()=>{
    if(closing)return;busy=false;U<Button>("Refresh").IsEnabled=true;U<Button>("Manage").IsEnabled=true;U<Button>("Cancel").Visibility=Visibility.Collapsed;
    if(t.IsFaulted){Notice("扫描失败，保留已有结果。",true);if(selfTest||capture!=null){EndTest(false,t.Exception.GetBaseException().ToString());}else Info(t.Exception.GetBaseException().Message);return;}
    if(t.Result.Cancelled){Notice("扫描已取消，保留上次完整清单。",true);return;}
    ApplyReport(t.Result,reason);if(selfTest)AdvanceTest();else if(capture!=null)EndTest(true,"实际本机盘点与窗口渲染通过。");
   }));});
  }
  string Fingerprint(ToolItem t){return json.Serialize(t);}
  void ApplyReport(Report next,string reason){
   if(next.Items==null)next.Items=new List<ToolItem>();if(next.Roots==null)next.Roots=new List<RootInfo>();if(next.Warnings==null)next.Warnings=new string[0];
   int added=0,missing=0,updated=0;
   if(hasSnapshot){var old=items.ToDictionary(x=>x.Id);var fresh=next.Items.ToDictionary(x=>x.Id);foreach(var t in next.Items){ToolItem before;if(!old.TryGetValue(t.Id,out before)){added++;Log("新发现："+t.Name,t.Path);}else if(Fingerprint(before)!=Fingerprint(t)){updated++;Log("资料或状态变化："+t.Name,t.Status);}}foreach(var t in items)if(!fresh.ContainsKey(t.Id)){missing++;Log("本次未再发现："+t.Name,"可能已移动、删除，或本次无法读取；工具管家没有删除文件。");}}
   report=next;items=next.Items.OrderBy(x=>Array.IndexOf(cats,x.Category)).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();hasSnapshot=true;lastScan=DateTime.UtcNow;
   if(selected!=null){var current=items.Find(x=>x.Id==selected.Id);if(current!=null)selected=current;}
   rendering=true;var client=U<ComboBox>("ClientFilter").SelectedItem as string;U<ComboBox>("ClientFilter").Items.Clear();U<ComboBox>("ClientFilter").Items.Add("所有客户端");foreach(var c in items.Select(x=>x.Client).Distinct().OrderBy(x=>x))U<ComboBox>("ClientFilter").Items.Add(c);U<ComboBox>("ClientFilter").SelectedItem=client!=null&&U<ComboBox>("ClientFilter").Items.Contains(client)?client:"所有客户端";rendering=false;
   Notice(string.Format("{0}完成 · {1} 条记录 · 新增 {2} / 本次未发现 {3} / 变化 {4} · 提示 {5} 条",reason,items.Count,added,missing,updated,report.Warnings.Length),report.Warnings.Length>0);
   Render();RebuildWatchers();
  }
  void Log(string title,string note){changes.Insert(0,new Change{Title=title,Note=DateTime.Now.ToString("HH:mm:ss")+" · "+note});if(changes.Count>250)changes.RemoveAt(changes.Count-1);}
  void Render(){
   U<Grid>("LibraryPage").Visibility=detail?Visibility.Collapsed:Visibility.Visible;U<Grid>("DetailPage").Visibility=detail?Visibility.Visible:Visibility.Collapsed;
   U<Button>("LibraryNav").Background=B(!showChanges?"#FFFFFF":"#F0F2F6");U<Button>("ChangesNav").Background=B(showChanges?"#FFFFFF":"#F0F2F6");
   if(detail){RenderDetail();return;}
   U<TextBlock>("PageTitle").Text=showChanges?"最近变化":"我的 AI 工具";U<TextBlock>("PageSubtitle").Text=showChanges?"每次刷新后，记录新发现、未再发现和资料变化。":"按工具类型整理，点击卡片进入详情，查看用途与使用方法。";
   U<UniformGrid>("CategoryHost").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;U<Grid>("Filters").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;
   U<ScrollViewer>("CardScroll").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;U<ScrollViewer>("ChangeScroll").Visibility=showChanges?Visibility.Visible:Visibility.Collapsed;
   U<Button>("Prev").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;U<Button>("Next").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;U<TextBlock>("Pager").Visibility=showChanges?Visibility.Collapsed:Visibility.Visible;
   if(showChanges){var host=U<StackPanel>("ChangeHost");host.Children.Clear();if(changes.Count==0)host.Children.Add(Text("还没有变化记录。首次盘点建立基准，之后新增或变化会显示在这里。",13,"#647087"));foreach(var c in changes){var sp=new StackPanel{Margin=new Thickness(16)};sp.Children.Add(Text(c.Title,14,"#1C2638"));var n=Text(c.Note,12,"#647087");n.Margin=new Thickness(0,8,0,0);sp.Children.Add(n);host.Children.Add(new Border{Background=Brushes.White,BorderBrush=B("#E3E7EE"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10),Child=sp});}}
   else{RenderCategories();RenderCards();}
   if(report!=null)U<TextBlock>("Summary").Text=string.Format("共 {0} 条 · 扫描 {1} 个目录 · 更新于 {2}",items.Count,report.Directories,report.ScannedAt);

  }
  void RenderCategories(){var host=U<UniformGrid>("CategoryHost");host.Children.Clear();for(int i=0;i<cats.Length;i++){string id=cats[i];var sp=new StackPanel();sp.Children.Add(Text(labels[i],12,"#647087"));var count=Text(items.Count(x=>x.Category==id).ToString(),25,"#1C2638");count.FontWeight=FontWeights.SemiBold;count.Margin=new Thickness(0,5,0,2);sp.Children.Add(count);sp.Children.Add(Text(subtitles[i],10,"#647087"));var b=Btn("");b.Content=sp;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Padding=new Thickness(12);b.Margin=new Thickness(0,0,i==5?0:8,0);if(category==id){b.Background=B("#EDF0FF");b.BorderBrush=B("#475BD4");}b.Click+=(s,e)=>{category=category==id?"all":id;pageIndex=0;Render();};host.Children.Add(b);}}
  void RenderCards(){
   if(detail)return;string q=U<TextBox>("Search").Text.Trim();string client=U<ComboBox>("ClientFilter").SelectedItem as string;
   U<TextBlock>("SearchHint").Visibility=q.Length==0?Visibility.Visible:Visibility.Collapsed;
   filtered=items.Where(t=>(category=="all"||t.Category==category)&&(client==null||client=="所有客户端"||t.Client==client)&&(q.Length==0||((t.Name+" "+t.Title+" "+t.Client+" "+t.Description+" "+t.Path).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0))).ToList();
   int pages=Math.Max(1,(filtered.Count+PageSize-1)/PageSize);pageIndex=Math.Max(0,Math.Min(pageIndex,pages-1));var host=U<WrapPanel>("CardHost");host.Children.Clear();
   foreach(var t in filtered.Skip(pageIndex*PageSize).Take(PageSize)){
    var sp=new StackPanel();var ico=new Border{Background=B("#F0F2F6"),CornerRadius=new CornerRadius(8),Width=32,Height=32,HorizontalAlignment=HorizontalAlignment.Left,Child=new TextBlock{Text=CatIcon(t.Category),Foreground=B("#647087"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=12}};sp.Children.Add(ico);
    var name=Text(t.Name,14,"#1C2638");name.FontWeight=FontWeights.SemiBold;name.Margin=new Thickness(0,12,0,4);sp.Children.Add(name);sp.Children.Add(Text(CatName(t.Category)+" · "+t.Client,11,"#647087"));
    string summary=t.Description??"";if(summary.Length>62)summary=summary.Substring(0,62)+"…";var desc=Text(summary,12,"#647087");desc.Margin=new Thickness(0,11,0,12);desc.Height=40;desc.LineHeight=19;desc.TextTrimming=TextTrimming.CharacterEllipsis;sp.Children.Add(desc);
    bool attention=t.Status.Contains("禁用")||t.Category=="downloads";var status=Text(t.Status,11,attention?"#966124":"#25705A");sp.Children.Add(new Border{Background=B(attention?"#FBF2E5":"#EAF5F0"),CornerRadius=new CornerRadius(4),Padding=new Thickness(6,3,6,3),HorizontalAlignment=HorizontalAlignment.Left,Child=status});var note=Text(t.Note,10,"#647087");note.Margin=new Thickness(0,6,0,0);sp.Children.Add(note);
    var action=Text("查看功能与使用方法  ↗",11,"#475BD4");action.Margin=new Thickness(0,12,0,0);sp.Children.Add(action);
    var button=Btn("");button.Content=sp;button.Tag=t.Id;button.Padding=new Thickness(15);button.Margin=new Thickness(0,0,10,12);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;button.Click+=(s,e)=>OpenTool(t);host.Children.Add(button);
   }
   ResizeCards();U<TextBlock>("ListTitle").Text=category=="all"?"全部工具":CatName(category);U<TextBlock>("FilteredCount").Text=filtered.Count+" 条 · 点击卡片进入详情";U<TextBlock>("Empty").Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;U<TextBlock>("Pager").Text=(pageIndex+1)+" / "+pages;U<Button>("Prev").IsEnabled=pageIndex>0;U<Button>("Next").IsEnabled=pageIndex+1<pages;
   U<TextBlock>("Summary").Text=report==null?"准备首次扫描…":string.Format("共 {0} 条 · 扫描 {1} 个目录 · 更新于 {2}",items.Count,report.Directories,report.ScannedAt);
  }
  void ResizeCards(){double width=U<ScrollViewer>("CardScroll").ActualWidth-24;if(width<100)return;var host=U<WrapPanel>("CardHost");host.Width=width;host.HorizontalAlignment=HorizontalAlignment.Left;int cols=width>850?3:2;double card=Math.Max(220,Math.Floor(width/cols)-12);foreach(FrameworkElement child in host.Children)child.Width=card;}
  void OpenTool(ToolItem t){selected=t;detail=true;locationTab=false;Render();}
  void RenderDetail(){if(selected==null)return;U<TextBlock>("Breadcrumb").Text="工具库 / "+CatName(selected.Category)+" / "+selected.Name;U<TextBlock>("DetailClient").Text=selected.Client;U<TextBlock>("DetailIcon").Text=CatIcon(selected.Category);U<TextBlock>("DetailTitle").Text=selected.Title??selected.Name;U<TextBlock>("DetailName").Text=selected.Name+" · "+CatName(selected.Category);U<TextBlock>("DetailEvidence").Text=selected.Evidence+(items.Any(x=>x.Id==selected.Id)?"":"\n本次未再发现这条记录，显示上次资料。");U<TextBlock>("Description").Text=selected.Description;U<TextBlock>("DocSource").Text=(selected.DocSource??"类型说明")+(string.IsNullOrEmpty(selected.DocPath)?"":"\n"+selected.DocPath);
   U<Button>("ReadDoc").IsEnabled=!string.IsNullOrEmpty(selected.DocPath)&&File.Exists(selected.DocPath);U<Button>("OpenRepo").Visibility=string.IsNullOrEmpty(selected.RepositoryUrl)?Visibility.Collapsed:Visibility.Visible;
   var host=U<StackPanel>("UsageHost");host.Children.Clear();int n=0;foreach(string u in selected.Usage??new string[0]){var row=new DockPanel{Margin=new Thickness(0,0,0,15)};var number=Text((++n).ToString(),12,"#475BD4");number.Width=24;row.Children.Add(number);var text=Text(u,13,"#536077");text.LineHeight=24;row.Children.Add(text);host.Children.Add(row);}
   U<TextBox>("DetailPath").Text=selected.Path;U<TextBlock>("DetailStatus").Text=selected.Status+" · "+selected.Note;U<TextBlock>("LocationEvidence").Text=selected.Evidence;U<Button>("Reveal").IsEnabled=IsLocal(selected.Path);U<Grid>("GuideBody").Visibility=locationTab?Visibility.Collapsed:Visibility.Visible;U<StackPanel>("LocationBody").Visibility=locationTab?Visibility.Visible:Visibility.Collapsed;U<Button>("GuideTab").Foreground=B(locationTab?"#647087":"#475BD4");U<Button>("LocationTab").Foreground=B(locationTab?"#475BD4":"#647087");
  }
  static bool IsLocal(string p){return p!=null&&System.Text.RegularExpressions.Regex.IsMatch(p,@"^[A-Za-z]:\\");}
  void Reveal(){try{if(selected==null||!IsLocal(selected.Path))return;string p=File.Exists(selected.Path)?System.IO.Path.GetDirectoryName(selected.Path):selected.Path;if(!Directory.Exists(p))throw new Exception("目录已不存在或无法访问。");Process.Start(new ProcessStartInfo("explorer.exe","\""+p+"\""){UseShellExecute=true});}catch(Exception ex){Info(ex.Message);}}
  void ReadDocumentation(){try{var f=new FileInfo(selected.DocPath);if(!f.Exists||f.Length>2*1024*1024||(f.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("文档已不存在、超过 2MB 或为链接。");var viewer=new Window{Title="原始说明 · "+selected.Name,Width=900,Height=700,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner};viewer.Content=new TextBox{Text=File.ReadAllText(f.FullName),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20),FontSize=13,FontFamily=new FontFamily("Consolas")};viewer.ShowDialog();}catch(Exception ex){Info(ex.Message);}}

  void ManageRoots(){
   var dialog=new Window{Title="管理目录",Width=800,Height=650,MinWidth=600,MinHeight=450,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#F5F6F9"),FontFamily=Window.FontFamily};
   var outer=new DockPanel{Margin=new Thickness(22)};dialog.Content=outer;
   var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(actions,Dock.Bottom);outer.Children.Add(actions);
   var add=Btn("＋ 添加工具目录");actions.Children.Add(add);var export=Btn("导出清单…");export.Margin=new Thickness(10,0,0,0);export.IsEnabled=report!=null;actions.Children.Add(export);var close=Btn("完成");close.Margin=new Thickness(10,0,0,0);actions.Children.Add(close);
   var content=new StackPanel();outer.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});bool changed=false;
   Action repaint=()=>{content.Children.Clear();var title=Text("这些位置会参与刷新与自动更新",19,"#1C2638");title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);var hint=Text("默认扫描客户端、下载目录和系统应用登记。添加文件夹后将记住设置；停止扫描只修改本工具的设置，不会移动或删除文件。",12,"#647087");hint.Margin=new Thickness(0,12,0,20);content.Children.Add(hint);
    foreach(var r in report==null?new List<RootInfo>():report.Roots.Where(r=>!settings.ExtraRoots.Contains(r.Path)).ToList()){content.Children.Add(Text(r.Label+" · 深度 "+r.Depth,12,"#1C2638"));var p=Text(r.Path,11,"#647087");p.Margin=new Thickness(0,4,0,12);content.Children.Add(p);}
    content.Children.Add(Text("自选目录",15,"#1C2638"));foreach(var p in settings.ExtraRoots.ToArray()){string local=p;var row=new DockPanel{Margin=new Thickness(0,12,0,0)};var stop=Btn("停止扫描");stop.FontSize=11;DockPanel.SetDock(stop,Dock.Right);row.Children.Add(stop);var label=Text(local,12,"#647087");label.Margin=new Thickness(0,8,10,0);row.Children.Add(label);content.Children.Add(row);stop.Click+=(s,e)=>{settings.ExtraRoots.Remove(local);changed=true;SaveSettings();repaintPlaceholder();};}
    var limits=Text("范围说明：不跟随目录链接；跳过依赖、构建与日志目录。自选目录深度 6；每轮最多 15000 个目录。下载、源码、编辑器扩展可能包含非 AI 工具。自动更新在窗口打开时工作，并每 60 秒补充核对应用登记。",12,"#647087");limits.Margin=new Thickness(0,22,0,0);content.Children.Add(limits);
   };
   repaintPlaceholder=repaint;repaint();
   add.Click+=(s,e)=>{using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description="选择工具、Skills、源码或下载目录",ShowNewFolderButton=false}){if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK){string p=System.IO.Path.GetFullPath(picker.SelectedPath);if(!IsLocal(p)){Info("当前仅支持本机磁盘目录。");return;}if(!settings.ExtraRoots.Contains(p,StringComparer.OrdinalIgnoreCase)){settings.ExtraRoots.Add(p);changed=true;SaveSettings();repaint();}}}};
   export.Click+=(s,e)=>Export();close.Click+=(s,e)=>dialog.Close();dialog.ShowDialog();repaintPlaceholder=null;if(changed)StartScan("目录范围已更新");
  }
  Action repaintPlaceholder;
  void Export(){var save=new SaveFileDialog{Title="导出全部本地清单",FileName="AI工具清单-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),Filter="JSON 清单 (*.json)|*.json|CSV 表格 (*.csv)|*.csv",OverwritePrompt=true};if(save.ShowDialog(Window)!=true)return;try{if(save.FilterIndex==1){File.WriteAllText(save.FileName,json.Serialize(report),new UTF8Encoding(true));}else{var b=new StringBuilder("名称,分类,本地发现情况,说明,客户端,准确路径\r\n");foreach(var t in items)b.AppendLine(string.Join(",",new[]{t.Name,CatName(t.Category),t.Status,t.Description,t.Client,t.Path}.Select(Csv)));File.WriteAllText(save.FileName,b.ToString(),new UTF8Encoding(true));}Info("清单已导出。文件包含本机路径与工具资料，分享前请自行查看。");}catch(Exception ex){Info("导出失败："+ex.Message);}}
  static string Csv(string s){s=s??"";if(System.Text.RegularExpressions.Regex.IsMatch(s,@"^\s*[=+@-]"))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
  void ShowWarnings(){string message=report==null?"尚未完成首次扫描。":"扫描目录："+report.Directories+"\n\n"+(report.Warnings.Length==0?"没有读取错误或范围上限提示。":string.Join("\n",report.Warnings))+"\n\n发现文件或配置不代表工具已加载、连接或能运行。清单不是全盘扫描；可从“管理目录”添加其他位置。";Info(message);}
  static void WriteShortcut(string file,string executable,string working){
   object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object shortcut=null;
   try{shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});Type type=shortcut.GetType();type.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{executable});type.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{working});type.InvokeMember("IconLocation",BindingFlags.SetProperty,null,shortcut,new object[]{executable+",0"});type.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"本地 AI 工具只读清单"});type.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
  }
  void CreateShortcut(){try{string desk=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);if(!Directory.Exists(desk))throw new Exception("无法找到桌面目录。");string target=System.IO.Path.Combine(desk,"AI工具管理器.lnk");if(File.Exists(target)&&MessageBox.Show(Window,"桌面已存在同名快捷方式，是否更新？","AI 工具管理器",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;WriteShortcut(target,Assembly.GetExecutingAssembly().Location,folder);Info("桌面快捷方式已创建。请保持 EXE 当前位置；移动后可重新创建快捷方式。");}catch(Exception ex){Info("快捷方式未创建："+ex.Message);}}
  void DisposeWatchers(){foreach(var w in watchers){try{w.EnableRaisingEvents=false;w.Dispose();}catch{}}watchers.Clear();}
  void RebuildWatchers(){DisposeWatchers();if(!settings.AutoUpdate||report==null||closing)return;var paths=new List<string>(report.Roots.Select(r=>r.Path));paths.Add(System.IO.Path.Combine(home,"AppData/Roaming/Claude"));paths.Add(System.IO.Path.Combine(home,"AppData/Roaming/Code/User"));paths.Add(System.IO.Path.Combine(home,".codeium/windsurf"));paths.Add(home);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p0 in paths){try{string p=p0;bool recursive=p0!=home;while(!Directory.Exists(p)){p=System.IO.Path.GetDirectoryName(p);recursive=false;if(string.IsNullOrEmpty(p))break;}if(string.IsNullOrEmpty(p)||!IsLocal(p)||!seen.Add(p+recursive)||(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)continue;var watcher=new FileSystemWatcher(p){IncludeSubdirectories=recursive,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=32768};watcher.Created+=(s,e)=>Changed(e.FullPath);watcher.Deleted+=(s,e)=>Changed(e.FullPath);watcher.Changed+=(s,e)=>Changed(e.FullPath);watcher.Renamed+=(s,e)=>Changed(e.FullPath);watcher.Error+=(s,e)=>Interlocked.Exchange(ref dirtyAt,DateTime.UtcNow.Ticks);watcher.EnableRaisingEvents=true;watchers.Add(watcher);}catch{Interlocked.Exchange(ref watcherFailed,1);}}}
  int watcherFailed;
  void Changed(string p){if(p.StartsWith(System.IO.Path.Combine(folder,"Data"),StringComparison.OrdinalIgnoreCase))return;if(System.Text.RegularExpressions.Regex.IsMatch(p,@"(?i)\\(node_modules|[.]git|[.]venv|venv|dist|build|__pycache__|GPUCache|[.]tmp|sessions|archived_sessions|logs|visualizations)(\\|$)"))return;Interlocked.Exchange(ref dirtyAt,DateTime.UtcNow.Ticks);}
  void Tick(){
   if(closing)return;
   if(selfTest&&!testEnded&&(DateTime.UtcNow-testStarted).TotalSeconds>100){EndTest(false,"自动更新验证超时，阶段 "+testStage);return;}
   if(Interlocked.Exchange(ref watcherFailed,0)==1)Notice("部分目录无法监控，仍可手动刷新，且会定期补充扫描。",true);
   if(!settings.AutoUpdate||busy||!hasSnapshot)return;long dirty=Interlocked.Read(ref dirtyAt);if(dirty>0&&(DateTime.UtcNow-new DateTime(dirty,DateTimeKind.Utc)).TotalMilliseconds>=1400){Interlocked.Exchange(ref dirtyAt,0);StartScan("检测到目录变化");}else if((DateTime.UtcNow-lastScan).TotalSeconds>=60){StartScan("自动核对");}
  }
  void Assert(bool test,string name){if(!test)throw new Exception("验证失败："+name);checks.Add(name);}
  void AdvanceTest(){try{
   if(testStage==0){
    string link=System.IO.Path.Combine(home,"test-shortcut.lnk");WriteShortcut(link,Assembly.GetExecutingAssembly().Location,folder);Assert(File.Exists(link)&&new FileInfo(link).Length>0,"快捷方式创建逻辑在隔离目录实际运行");
    U<CheckBox>("AutoUpdate").IsChecked=false;Assert(!json.Deserialize<Settings>(File.ReadAllText(settingsFile)).AutoUpdate,"自动更新设置真实保存");U<CheckBox>("AutoUpdate").IsChecked=true;Assert(json.Deserialize<Settings>(File.ReadAllText(settingsFile)).ExtraRoots.Contains(System.IO.Path.Combine(home,"tools")),"自选目录保存并可读取");
    Assert(items.Any(x=>x.Category=="skills")&&items.Any(x=>x.Category=="mcp")&&items.Any(x=>x.Category=="projects"),"真实隔离文件被扫描并分类");
    Window.UpdateLayout();var layoutCards=U<WrapPanel>("CardHost").Children.OfType<Button>().ToArray();Assert(layoutCards.Length>2&&Math.Abs(layoutCards[0].TranslatePoint(new Point(0,0),U<WrapPanel>("CardHost")).Y-layoutCards[2].TranslatePoint(new Point(0,0),U<WrapPanel>("CardHost")).Y)<2,"桌面宽度下使用三列卡片");
    U<TextBox>("Search").Text="sample-skill";RenderCards();Assert(filtered.Count==1,"搜索筛选");var button=U<WrapPanel>("CardHost").Children.OfType<Button>().First();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(detail&&U<Grid>("LibraryPage").Visibility==Visibility.Collapsed,"整张卡片进入独立详情页");Assert(U<TextBlock>("Description").Text.Contains("真实文档摘要"),"详情来自本地文档");U<Button>("LocationTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(U<StackPanel>("LocationBody").Visibility==Visibility.Visible,"本地位置标签");U<Button>("Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!detail&&U<TextBox>("Search").Text=="sample-skill","返回保留搜索状态");U<TextBox>("Search").Text="";testStage=1;File.WriteAllText(System.IO.Path.Combine(home,"Downloads/manual-new.zip"),"fixture only");U<Button>("Refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   }else if(testStage==1){Assert(items.Any(x=>x.Name=="manual-new.zip"),"手动刷新发现新增文件");Assert(changes.Any(c=>c.Title.Contains("manual-new.zip")),"最近变化记录新增");testStage=2;File.WriteAllText(System.IO.Path.Combine(home,"Downloads/auto-new.zip"),"fixture only");
   }else if(testStage==2){if(!items.Any(x=>x.Name=="auto-new.zip"))return;Assert(true,"FileSystemWatcher 真实事件触发自动更新");testStage=3;string skill=System.IO.Path.Combine(home,".codex/skills/sample-skill/SKILL.md");File.AppendAllText(skill,"\n# Usage\nDocument updated after launch.");
   }else if(testStage==3){Assert(changes.Any(c=>c.Title.Contains("资料或状态变化：sample-skill")),"文档变化被重新读取");testStage=4;File.Delete(System.IO.Path.Combine(home,"Downloads/auto-new.zip"));
   }else if(testStage==4){if(items.Any(x=>x.Name=="auto-new.zip"))return;Assert(changes.Any(c=>c.Title.Contains("本次未再发现：auto-new.zip")),"外部删除后清单更新");Assert(File.Exists(System.IO.Path.Combine(home,"Downloads/manual-new.zip")),"扫描没有删除其他文件");EndTest(true,"原生界面、真实扫描与自动更新通过。");}
  }catch(Exception ex){EndTest(false,ex.ToString());}}
  void EndTest(bool success,string message){if(testEnded)return;testEnded=true;ExitCode=success?0:1;try{if(capture!=null){detail=false;showChanges=false;Render();Window.UpdateLayout();var element=U<Grid>("RootView");var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var s=File.Create(capture))encoder.Save(s);}if(testOutput!=null)File.WriteAllText(testOutput,json.Serialize(new{Success=success,Message=message,Checks=checks,ItemCount=items.Count,CategoryCounts=items.GroupBy(t=>t.Category).ToDictionary(g=>g.Key,g=>g.Count()),Directories=report==null?0:report.Directories,Warnings=report==null?new string[0]:report.Warnings}),new UTF8Encoding(true));}catch(Exception ex){ExitCode=1;if(testOutput!=null)File.WriteAllText(testOutput,ex.ToString());}Window.Close();}
 }
}
