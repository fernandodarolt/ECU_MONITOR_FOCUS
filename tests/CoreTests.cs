using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Monitor9M55AD;

public static class CoreTests {
 static int count=0;
 static void Check(bool yes,string name){count++;if(!yes)throw new Exception("FAIL: "+name);}
 static void Throws(Action a,string name){bool hit=false;try{a();}catch{hit=true;}Check(hit,name);}
 static string Frame(byte[] data){return "7E8 "+data.Length.ToString("X2")+" "+BitConverter.ToString(data).Replace('-',' ')+"\r>";}
 static byte[] FloatBE(float f){byte[] x=BitConverter.GetBytes(f);if(BitConverter.IsLittleEndian)Array.Reverse(x);return x;}
 public static int Run(){
  count+=InitializationTests.Run();
  Check(ReadOnlyProfile.Allowed("2209D4"),"APP9M55AD PID read allowed");
  Check(ReadOnlyProfile.Allowed("22093C")&&ReadOnlyProfile.Allowed("22091A"),"actual/desired throttle PID reads allowed");
  Check(ReadOnlyProfile.Allowed("010E")&&ReadOnlyProfile.Allowed("0111")&&ReadOnlyProfile.Allowed("0103"),"spark TP and loop status reads allowed");
  Check(!ReadOnlyProfile.Allowed("2209D400")&&!ReadOnlyProfile.Allowed("2209D5"),"only exact new native reads allowed");
  Check(ReadOnlyProfile.Allowed("23003F9E040004"),"fixed pulse request allowed");
  Check(ReadOnlyProfile.Allowed("23003FAA700004"),"fixed modeled pressure allowed");
  foreach(string s in new[]{"04","1101","1003","2701","2E123400","3101","3D003F9E040004","23003F9E040008","ATSH7E1","ATI\r04","","ATPP01SV00"})
   Check(!ReadOnlyProfile.Allowed(s),"unsafe or unlisted command rejected "+s);
  byte[] p=ElmCodec.Payload("23003F9E040004\r7E8 05 63 3B 44 9B A6\r>");
  Check(p.Length==5&&p[0]==0x63,"single frame with echo");
  Check(Math.Abs(ElmCodec.FloatMemory(p,0,100)*1000-3.0)<0.001,"BE seconds to milliseconds");
  byte[] dlc=ElmCodec.Payload("7E8 8 05 63 3B 44 9B A6 00 00\r>");
  Check(dlc.Length==5,"optional DLC parsed");
  Check(ElmCodec.Payload("7E8 05 63 3B 44 9B A6 00 00\r7E9 03 41 05 40\r>").Length==5,"other ECU ignored");
  Throws(delegate{ElmCodec.Payload("7E9 05 63 3B 44 9B A6\r>");},"wrong source only rejected");
  Throws(delegate{ElmCodec.Payload("7E8 05 63 3B 44\r>");},"short SF rejected");
  Throws(delegate{ElmCodec.Payload("NO DATA\r>");},"NO DATA is not zero");
  Throws(delegate{ElmCodec.FloatMemory(ElmCodec.Payload("7E8 03 7F 23 31\r>"),0,100);},"NRC denied rejected");
  Throws(delegate{ElmCodec.FloatMemory(ElmCodec.Payload("7E8 03 7F 23 78\r>"),0,100);},"NRC pending not shown as value");
  Throws(delegate{ElmCodec.FloatMemory(new byte[]{0x63,0x7f,0xc0,0,0},0,100);},"NaN rejected");
  Throws(delegate{ElmCodec.FloatMemory(new byte[]{0x63,0x7f,0x80,0,0},0,100);},"infinity rejected");
  Throws(delegate{ElmCodec.FloatMemory(new byte[]{0x63,0xbf,0x80,0,0},0,100);},"negative pulse rejected");
  Throws(delegate{ElmCodec.FloatMemory(new byte[]{0x62,0,0,0,0},0,100);},"wrong response SID rejected");
  Throws(delegate{ElmCodec.FloatMemory(new byte[]{0x63,0,0,0},0,100);},"truncated data rejected");
  Check(ElmCodec.UIntMemory(new byte[]{0x63,0,0,2,0xd0},2040)==720,"offset microseconds BE");
  Throws(delegate{ElmCodec.UIntMemory(new byte[]{0x63,0,0,8,0},2040);},"offset over profile cap rejected");
  string multi="7E8 10 11 63 00 00 0B B8 00\r7E8 21 00 0B B8 00 00 0B B8\r7E8 22 00 00 0B B8 00 00 00\r>";
  byte[] many=ElmCodec.Payload(multi);
  Check(many.Length==17&&many[0]==0x63,"ISO-TP complete 16-byte array");
  double[] cyl=ElmCodec.Cylinders(many);
  Check(cyl.Length==4&&cyl[0]==3&&cyl[3]==3,"four cylinder microseconds");
  Throws(delegate{ElmCodec.Payload(multi.Replace("7E8 21","7E8 23"));},"bad multiframe sequence rejected");
  Throws(delegate{ElmCodec.Payload("7E8 10 11 63 00 00 0B B8 00\r>");},"incomplete multiframe rejected");
  Throws(delegate{ElmCodec.Payload("7E8 10 11 63 00 00 00 00 00\r7E8 21 00 00 00 00 00 00\r7E8 22 00 00 00 00 00 00 00\r>");},"short intermediate CF cannot promote padding to cylinder data");
  Check(ElmCodec.Obd(ElmCodec.Payload("7E8 04 41 0C 0B B8\r>"),0x0c)==750,"RPM decode");
  Check(ElmCodec.Obd(ElmCodec.Payload("7E8 03 41 05 7D\r>"),5)==85,"ECT decode");
  Check(ElmCodec.Obd(ElmCodec.Payload("7E8 03 41 06 80\r>"),6)==0,"trim decode");
  Check(ElmCodec.Obd(new byte[]{0x41,14,168},14)==20,"spark advance positive degrees");
  Check(ElmCodec.Obd(new byte[]{0x41,14,108},14)==-10,"spark retard negative degrees");
  Check(Math.Abs(ElmCodec.Obd(new byte[]{0x41,17,210},17)-82.35294117647)<1e-8,"absolute throttle percent not degrees");
  Check(ElmCodec.Obd(new byte[]{0x41,3,2,0},3)==2,"fuel loop status bank1 raw flags");
  Check(ElmCodec.Obd(new byte[]{0x41,6,148},6)==15.625,"STFTpositive fuel correction");
  Check(ElmCodec.Obd(new byte[]{0x41,7,112},7)==-12.5,"LTFTnegative fuel correction");
  Check(ElmCodec.NativeScaled(new byte[]{0x62,9,0xd4,156},0x09d4,1,2)==78,"native APP78 percent retains ECU scale");
  Check(ElmCodec.NativeScaled(new byte[]{0x62,9,0xd4,255},0x09d4,1,2)==127.5,"APP is not silently clamped to100");
  Check(ElmCodec.NativeScaled(new byte[]{0x62,9,0x3c,0xa6,0},0x093c,2,512)==83,"actual throttle fixed point512");
  Check(ElmCodec.NativeScaled(new byte[]{0x62,9,0x1a,0xa4,0},0x091a,2,512)==82,"desired throttle fixed point512");
  Throws(delegate{ElmCodec.NativeScaled(new byte[]{0x62,9,0xd5,156},0x09d4,1,2);},"wrong APP DID rejected");
  Throws(delegate{ElmCodec.NativeScaled(new byte[]{0x62,9,0x3c,0xa6},0x093c,2,512);},"truncated actual angle rejected");
  Throws(delegate{ElmCodec.NativeScaled(new byte[]{0x7f,0x22,0x31},0x09d4,1,2);},"native NRC remains rejection");
  Check(Math.Abs(ElmCodec.CompoundTrim(10,10)-21)<1e-10,"compound trims are multiplicative not addition");
  Check(Math.Abs(ElmCodec.CompoundTrim(15.625,-12.5)-1.171875)<1e-10,"mixed sign compound correct");
  Throws(delegate{ElmCodec.Obd(new byte[]{0x41,0x0d,1},0x0c);},"wrong PID rejected");
  Throws(delegate{new ConnectOptions{Port="COM4\rATZ",ProfileConfirmed=true}.Validate();},"port injection rejected");
  Throws(delegate{new ConnectOptions{Port="COM4",ProfileConfirmed=false}.Validate();},"profile confirmation required");
  Throws(delegate{new ConnectOptions{Port="COM4",ProfileConfirmed=true,IntervalMs=1}.Validate();},"rate limit");
  string dir=Path.Combine(Path.GetTempPath(),"9M55AD-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(dir);
  MonitorEngine engine=new MonitorEngine(dir);
  engine.Start(new ConnectOptions{Demo=true,ProfileConfirmed=true,IntervalMs=250,Context=true,Cylinders=true});
  DateTime deadline=DateTime.UtcNow.AddSeconds(8);
  while(engine.Sequence<2&&DateTime.UtcNow<deadline)Thread.Sleep(50);
  engine.Stop();
  string json=engine.SnapshotJson();
  Check(engine.Sequence>=2,"demo engine produced timed samples");
  Check(json.Contains("DEMO")&&!json.Contains("\"liveValidated\":true"),"demo never claims real validation");
  Check(File.Exists(engine.LogPath),"CSV created");
  string csv=File.ReadAllText(engine.LogPath);
  Check(csv.Contains("pressure_model_psi")&&csv.Contains("pulse_base_ms")&&csv.Contains("DEMO"),"CSV signals and mode labels");
  Check(csv.Contains("pulse_base_ms_utc")&&csv.Contains("pressure_model_psi_status"),"per-signal time and quality exported");
  foreach(string id in new[]{"spark_deg","app_pct","throttle_pct","etc_actual_deg","etc_desired_deg","stft_pct","ltft_pct","total_trim_pct","fuelsys1_code"})
   Check(csv.Contains(id+",")&&csv.Contains(id+"_utc")&&csv.Contains(id+"_status")&&csv.Contains(id+"_age_ms"),"unified CSV metadata "+id);
  Check(rawField(json,"spark_deg")&&rawField(json,"app_pct")&&rawField(json,"total_trim_pct"),"unified signals in every demo cycle");
  string raw=File.ReadAllText(engine.RawLogPath);
  Check(raw.Contains("23003F9E040004")&&raw.Contains("23003FAA700004"),"raw transaction audit");
  Check(!raw.Contains("\"command\":\"27")&&!raw.Contains("\"command\":\"10"),"no unlocking or session changes");
  MonitorEngine denied=new MonitorEngine(dir,delegate(string port,int baud){return new DemoChannel(true);});
  denied.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
  deadline=DateTime.UtcNow.AddSeconds(5);
  while(denied.Sequence<2&&denied.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(50);
  denied.Stop();
  Check(denied.Sequence>=2&&denied.State!="error","RAM denial preserves useful unified PID capture");
  Check(denied.SnapshotJson().Contains("0x31"),"NRC preserved for diagnosis");
  Check(denied.SnapshotJson().Contains("\"ramAvailable\":false"),"denied RAM never claims available");
  string deniedRaw=File.ReadAllText(denied.RawLogPath);
  Check(deniedRaw.Split(new[]{"23003F9E040004"},StringSplitOptions.None).Length==2,"denied RAM requested just once");
  Check(deniedRaw.Contains("010E")&&deniedRaw.Contains("0106")&&deniedRaw.Contains("2209D4"),"OBD/native reads remain without FORScan");
  MonitorEngine contextDenied=new MonitorEngine(dir,delegate(string port,int baud){return new ContextDeniedChannel();});
  contextDenied.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250,Context=true});
  deadline=DateTime.UtcNow.AddSeconds(5);
  while(contextDenied.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(30);
  contextDenied.Stop();
  Check(contextDenied.State=="error"&&contextDenied.Sequence==0,"NRC in optional context terminates capture too");
  MonitorEngine switching=new MonitorEngine(dir,delegate(string port,int baud){return new LoopSwitchChannel();});
  switching.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
  deadline=DateTime.UtcNow.AddSeconds(5);
  while(switching.Sequence<3&&switching.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(30);
  switching.Stop();
  string switched=switching.SnapshotJson();
  Check(switching.Sequence>=3&&switched.Contains("inactive_ol"),"CL to OL invalidates compound trim");
  var obj=(Dictionary<string,object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(switched);
  var vv=(Dictionary<string,object>)obj["values"];
  Check(((Dictionary<string,object>)vv["total_trim_pct"])["value"]==null,"OL never reuses last CL compound value");
  string switchedCsv=File.ReadAllText(switching.LogPath);
  Check(switchedCsv.Contains("inactive_ol"),"OL compound availability exported in CSV");
  MonitorEngine absent=new MonitorEngine(dir,delegate(string port,int baud){return new MissingTrimChannel();});
  absent.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
  deadline=DateTime.UtcNow.AddSeconds(5);
  while(absent.Sequence<2&&absent.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(30);
  absent.Stop();
  string absentJson=absent.SnapshotJson();
  Check(absent.Sequence>=2&&absentJson.Contains("unavailable"),"missing STFT preserves capture but derived trim unavailable");
  string absentRaw=File.ReadAllText(absent.RawLogPath);
  Check(absentRaw.Split(new[]{"\"command\":\"0106\""},StringSplitOptions.None).Length>=3,"NO DATA STFT retried without zero fill");
  foreach(bool persistent in new[]{false,true}){
   MonitorEngine retry=new MonitorEngine(dir,delegate(string port,int baud){return new RpmGapChannel(persistent);});
   retry.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
   deadline=DateTime.UtcNow.AddSeconds(6);
   while(retry.Sequence<3&&retry.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(20);
   retry.Stop();
   string gapCsv=File.ReadAllText(retry.LogPath);
   Check(persistent?retry.State=="error":retry.State!="error"&&retry.Sequence>=3,"RPM NO DATA bounded recovery "+persistent);
   Check(gapCsv.Contains("no_data"),"RPM gap explicitly missing in CSV "+persistent);
  }
  Check(csv.Contains("pressure_model_bar"),"pressure exported in bar as well as psi");
  MonitorEngine memoryGap=new MonitorEngine(dir,delegate(string port,int baud){return new MemoryGapChannel();});
  memoryGap.Start(new ConnectOptions{Port="COM999",Baud=38400,ProfileConfirmed=true,IntervalMs=250});
  deadline=DateTime.UtcNow.AddSeconds(5);
  while(memoryGap.Sequence<2&&memoryGap.State!="error"&&DateTime.UtcNow<deadline)Thread.Sleep(20);
  memoryGap.Stop();
  Check(memoryGap.Sequence>=2&&memoryGap.State!="error","isolated RAM NO DATA preserves capture");
  Check(File.ReadAllText(memoryGap.LogPath).Contains("no_data")&&memoryGap.SnapshotJson().Contains("\"ramAvailable\":true"),"RAM retries and recovers instead of session disable");

  Console.WriteLine("PASS "+count+" checks; artifacts "+dir);
  return count;
 }
 static bool rawField(string s,string name){return s.Contains("\""+name+"\":{");}
}
public sealed class ContextDeniedChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();bool pending=false;
 public void Open(){inner.Open();}public void Dispose(){inner.Dispose();}
 public void WriteLine(string c){pending=c=="0105";if(!pending)inner.WriteLine(c);}
 public string ReadChunk(){if(pending){pending=false;return "7E8 03 7F 01 31\r>";}return inner.ReadChunk();}
}
public sealed class LoopSwitchChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();int loops=0;bool pending=false;
 public void Open(){inner.Open();}public void Dispose(){inner.Dispose();}
 public void WriteLine(string c){pending=c=="0103";if(pending)loops++;else inner.WriteLine(c);}
 public string ReadChunk(){if(pending){pending=false;return loops==1?"7E8 04 41 03 02 00\r>":"7E8 04 41 03 04 00\r>";}return inner.ReadChunk();}
}
public sealed class MissingTrimChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();bool pending=false;
 public void Open(){inner.Open();}public void Dispose(){inner.Dispose();}
 public void WriteLine(string c){pending=c=="0106";if(!pending)inner.WriteLine(c);}
 public string ReadChunk(){if(pending){pending=false;return "NO DATA\r>";}return inner.ReadChunk();}
}

public sealed class RpmGapChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();readonly bool persistent;int rpm;bool pending;
 public RpmGapChannel(bool p){persistent=p;}public void Open(){}public void Dispose(){}
 public void WriteLine(string c){if(c=="010C")rpm++;pending=c=="010C"&&rpm>=2&&(persistent||rpm==2);if(!pending)inner.WriteLine(c);}
 public string ReadChunk(){if(pending){pending=false;return "NO DATA\r>";}return inner.ReadChunk();}
}

public sealed class MemoryGapChannel:IElmChannel {
 readonly DemoChannel inner=new DemoChannel();bool pending;bool injected;
 public void Open(){}public void Dispose(){}
 public void WriteLine(string c){pending=c=="23003FAA700004"&&!injected;if(pending)injected=true;else inner.WriteLine(c);}
 public string ReadChunk(){if(pending){pending=false;return "NO DATA\r>";}return inner.ReadChunk();}
}
