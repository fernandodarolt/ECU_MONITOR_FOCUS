using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Monitor9M55AD;

// Offline channels only. This suite never constructs SerialChannel.
public static class InitializationTests {
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL initialization: "+name);}
 static Dictionary<string,object> Snapshot(MonitorEngine e){return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(e.SnapshotJson());}
 static MonitorEngine Capture(DiscoveryChannel fake,bool expectSuccess){
  string dir=Path.Combine(Path.GetTempPath(),"9M55AD-init-"+Guid.NewGuid().ToString("N"));
  MonitorEngine e=new MonitorEngine(dir,delegate(string p,int b){return fake;});
  e.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
  DateTime end=DateTime.UtcNow.AddSeconds(4);
  while(e.Sequence<1&&e.State!="error"&&DateTime.UtcNow<end)Thread.Sleep(10);
  e.Stop();
  Check(expectSuccess?e.Sequence>=1&&e.State!="error":e.State=="error"&&e.Sequence==0,fake.Scenario+" validation result");
  return e;
 }
 public static int Run(){
  checks=0;
  DiscoveryChannel functional=new DiscoveryChannel("physical_nrc");MonitorEngine functionalCapture=Capture(functional,true);
  Check(functional.Commands.Count(x=>x=="ATSH7DF")>1,"physical OBD refusal falls back to functional addressing");
  Check(functional.Commands.Contains("23003F9E040004"),"functional RPM validates capture before physical RAM");
  Check(functional.Commands.Count(x=>x.StartsWith("ATSH"))<=4+2*functionalCapture.Sequence,"grouped requests require at most two header switches per captured cycle");
  string lastHeader="";int rpmRequests=0;
  foreach(string command in functional.Commands){
   if(command.StartsWith("ATSH"))lastHeader=command;
   if(command.StartsWith("23")||command.StartsWith("22"))Check(lastHeader=="ATSH7E0","native read remains physical "+command);
   if(command.StartsWith("01")&&command!="0100"){
    if(command=="010C")rpmRequests++;
    if(rpmRequests>1)Check(lastHeader=="ATSH7DF","OBD read remains functional after fallback "+command);
   }
  }
  DiscoveryChannel legacy=new DiscoveryChannel("legacy_filter");Capture(legacy,true);
  Check(legacy.Commands.Contains("ATAR")&&legacy.Commands.IndexOf("ATAR")<legacy.Commands.IndexOf("0100"),"legacy CRA rejection uses documented AR before discovery");
  foreach(string filterFailure in new[]{"filter_unsupported","filter_malformed"}){
   DiscoveryChannel badFilter=new DiscoveryChannel(filterFailure);Capture(badFilter,false);
   Check(!badFilter.Commands.Contains("0100"),"filter failure never starts discovery "+filterFailure);
   Check(badFilter.Commands.Contains("ATAR")== (filterFailure=="filter_unsupported"),"only explicit question mark allows AR fallback");
  }
  DiscoveryChannel normal=new DiscoveryChannel("success6");MonitorEngine good=Capture(normal,true);
  string[] sent=normal.Commands.ToArray();int discovery=Array.IndexOf(sent,"0100"),physical=Array.IndexOf(sent,"ATSH7E0");
  Check(discovery>=0&&discovery<physical,"functional 0100 precedes physical header");
  Check(Array.IndexOf(sent,"ATDPN")>discovery&&Array.IndexOf(sent,"ATDPN")<physical,"DPN checked only after valid functional response");
  Check(Array.IndexOf(sent,"ATM0")>Array.IndexOf(sent,"ATWS")&&Array.IndexOf(sent,"ATM0")<Array.IndexOf(sent,"ATTP6"),"disable protocol memory after warm start before ATTP can validate");
  Check(Array.IndexOf(sent,"ATWS")>=0&&Array.IndexOf(sent,"ATWS")<Array.IndexOf(sent,"ATE0"),"warm-start banner consumed before echo configuration");
  Check(Array.IndexOf(sent,"010C")>physical&&Array.IndexOf(sent,"010C")<Array.IndexOf(sent,"23003F9E040004"),"physical RPM gates RAM capture");
  Check((string)Snapshot(good)["protocol"]=="6"&& !(bool)Snapshot(good)["obdValidated"],"confirmed protocol exposed separately from stage");
  var stopped=Snapshot(good);Check(!(bool)stopped["adapterConnected"]&&!(bool)stopped["obdValidated"]&&!(bool)stopped["liveValidated"]&&(string)stopped["connectionStage"]=="stopped","disposed connection never claims current USB or ECU validation");
  string audit=File.ReadAllText(good.RawLogPath);
  Check(audit.Contains("ATI")&&audit.Contains("ATWS")&&audit.Contains("0100")&&audit.Contains("ATDPN"),"initialization transactions preserved in raw log");
  foreach(string cmd in new[]{"0100","ATWS","ATTP6","ATTP8","ATDPN","ATCRA","ATSH7DF"})Check(ReadOnlyProfile.Allowed(cmd),"required command allowed "+cmd);
  foreach(string cmd in new[]{"ATSP0","ATSP6","ATSP8","ATTP7","ATTP9","ATM1","ATPP01SV00","ATWR","ATSH7E1","1003","2701"})Check(!ReadOnlyProfile.Allowed(cmd),"unneeded or unsafe command denied "+cmd);
  foreach(string name in new[]{"successA6","success8","successA8","other_then_pcm"}){
   DiscoveryChannel ch=new DiscoveryChannel(name);MonitorEngine e=Capture(ch,true);var status=Snapshot(e);
   Check((string)status["protocol"]==(name.Contains("8")?"8":"6"),name+" normalized protocol");
   if(name.Contains("8")){Check(ch.Commands.Contains("ATTP6")&&ch.Commands.Contains("ATTP8"),name+" bounded second protocol attempted");Check(ch.Commands.Count(x=>x=="0100")==2,name+" discovers each protocol once");}
  }
  foreach(string name in new[]{"no_pcm","no_rpm","nrc","bad_dpn","physical_other_nrc","functional_nrc","unsupported_try","bad_warmstart","no_data"}){
   DiscoveryChannel ch=new DiscoveryChannel(name);MonitorEngine e=Capture(ch,false);var status=Snapshot(e);
   Check(!(bool)status["liveValidated"]&&!(bool)status["obdValidated"]&&!((bool)status["ramAvailable"]),name+" never validates ECU");
   Check(!(bool)status["adapterConnected"]&&(string)status["connectionStage"]=="failed",name+" failed channel is closed");
   if(name=="bad_dpn"||name=="physical_other_nrc"||name=="functional_nrc")Check(!ch.Commands.Contains("ATTP8"),name+" confirmation failure never switches a positively discovered bus");
   Check(!ch.Commands.Any(x=>x.StartsWith("23")||x.StartsWith("22")||x=="010E"),name+" no capture commands before validation");
   Check(File.ReadAllLines(e.LogPath).Length==1,name+" CSV remains header only");
   Check(File.ReadAllText(e.RawLogPath).Contains("ATI"),name+" raw log survives failed initialization");
   if(name=="no_pcm"||name=="no_data"||name=="nrc")Check((string)status["message"]!=null&&((string)status["message"]).Contains("USB"),name+" distinguishes adapter from vehicle response");
   if(name=="unsupported_try")Check(((string)status["message"]).Contains("ATTP")&&!ch.Commands.Contains("ATTP8"),"unsupported ATTP stops without permanent protocol fallback");
  }
  DiscoveryChannel silent=new DiscoveryChannel("silent");
  string folder=Path.Combine(Path.GetTempPath(),"9M55AD-cancel-"+Guid.NewGuid().ToString("N"));MonitorEngine cancel=new MonitorEngine(folder,delegate(string p,int b){return silent;});
  cancel.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true});
  Check(silent.Discovery.WaitOne(2000),"discovery entered before cancellation");Stopwatch sw=Stopwatch.StartNew();cancel.Stop();
  Check(sw.ElapsedMilliseconds<1800,"cancel interrupts 8 second discovery promptly");
  Check(cancel.State=="stopped"&&cancel.Sequence==0&&!silent.Commands.Any(x=>x.StartsWith("23")),"cancel never starts capture");
  Check((string)Snapshot(cancel)["connectionStage"]=="cancelled"&&File.ReadAllLines(cancel.LogPath).Length==1,"cancel stage and no prevalidation CSV row");
  return checks;
 }
}

public sealed class DiscoveryChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();string queued=null,protocol="6";bool physical;
 public readonly string Scenario;public readonly List<string> Commands=new List<string>();public readonly ManualResetEvent Discovery=new ManualResetEvent(false);
 public DiscoveryChannel(string scenario){Scenario=scenario;}
 public void Open(){}public void Dispose(){}
 public void WriteLine(string command){
  lock(Commands)Commands.Add(command);
  if(command=="ATAR"&&Scenario=="filter_unsupported"){queued="?\r>";return;}
  if(command=="ATCRA"&&Scenario=="filter_malformed"){queued="CAN ERROR\r>";return;}
  if(command=="ATCRA"&&(Scenario=="legacy_filter"||Scenario=="filter_unsupported")){queued="?\r>";return;}
  if(command=="ATWS"){queued=Scenario=="bad_warmstart"?"?\r>":"ATWS\rELM327 TEST DOUBLE\r>";return;}
  if(command=="ATTP6"||command=="ATTP8"){protocol=command.Substring(4);queued=Scenario=="unsupported_try"?"?\r>":"OK\r>";return;}
  if(command=="ATSH7DF")physical=false;if(command=="ATSH7E0")physical=true;
  if(command=="ATDPN"){queued=Scenario=="bad_dpn"?"A7\r>":(Scenario=="successA6"||Scenario=="successA8"?"A":"")+protocol+"\r>";return;}
  if(command=="0100"){
   Discovery.Set();
   if(Scenario=="silent"){queued="";return;}
   if(Scenario=="success8"||Scenario=="successA8"){if(protocol=="6"){queued="CAN ERROR\r>";return;}}
   if(Scenario=="no_pcm"){queued="7E9 06 41 00 00 10 00 00\r>";return;}
   if(Scenario=="no_data"){queued="NO DATA\r>";return;}
   if(Scenario=="no_rpm"){queued="7E8 06 41 00 00 00 00 00\r>";return;}
   if(Scenario=="nrc"){queued="7E8 03 7F 01 11\r>";return;}
   queued=(Scenario=="other_then_pcm"?"7E9 06 41 00 FF FF FF FF\r":"")+"7E8 06 41 00 00 10 00 00\r>";return;
  }
  if(command=="010C"&&Scenario=="physical_other_nrc"){queued="7E8 03 7F 01 22\r>";return;}
  if(command=="010C"&&Scenario=="functional_nrc"){queued="7E8 03 7F 01 11\r>";return;}
  if(command=="010C"&&physical&&Scenario=="physical_nrc"){queued="7E8 03 7F 01 11\r>";return;}
  inner.WriteLine(command);
 }
 public string ReadChunk(){if(queued!=null){string answer=queued;queued=null;return answer;}return inner.ReadChunk();}
}
