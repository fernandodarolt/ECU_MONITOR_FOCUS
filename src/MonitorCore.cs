using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Monitor9M55AD {
 public sealed class ElmNoDataException:IOException {public ElmNoDataException():base("ELM: NO DATA") {}}
 public sealed class DiagnosticRefusalException:IOException {
  public readonly byte Service,Code;
  public DiagnosticRefusalException(byte service,byte code):base("NRC 0x"+code.ToString("X2")+" no serviço 0x"+service.ToString("X2")+"; leitura não concluída/recusada. Nenhum desbloqueio será tentado."){Service=service;Code=code;}
 }
 public static class ReadOnlyProfile {
  public static readonly string[] Init={"ATE0","ATM0","ATL0","ATS1","ATH1","ATCAF1","ATCFC1","ATST64","ATAT1"};
  public static bool Allowed(string command) {
   if(command==null||!Regex.IsMatch(command,"^[A-Z0-9]+$"))return false;
   return command=="ATI"||Init.Contains(command)||new[]{"ATWS","ATTP6","ATTP8","ATDPN","ATAR","ATCRA","ATCRA7E8","ATSH7DF","ATSH7E0","0100","010C","0105","010F","010B","0104","0106","0107","0103","010E","0111","2209D4","22093C","22091A","23003F9E040004","23003F9DDC0004","23003FAA700004","23003F89840010"}.Contains(command);
  }
  public static void Guard(string command){if(!Allowed(command))throw new InvalidOperationException("Comando fora da lista de leitura; bloqueado.");}
 }
 public static class ElmCodec {
  public static byte[] Payload(string raw) {
   if(raw==null)throw new IOException("Resposta vazia.");
   List<byte[]> complete=new List<byte[]>();List<byte> assembly=null;int wanted=0,sequence=1;
   foreach(string original in raw.Replace('>',' ').Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
    string line=original.Trim().ToUpperInvariant();if(line.Length==0)continue;
    if(ReadOnlyProfile.Allowed(line.Replace(" ","")))continue;
    if(line.StartsWith("SEARCHING")||line=="OK")continue;
    if(line.Contains("NO DATA"))throw new ElmNoDataException();
    if(line.Contains("STOPPED")||line.Contains("ERROR")||line.Contains("UNABLE")||line.Contains("BUFFER FULL")||line=="?")throw new IOException("ELM: "+line);
    Match match=Regex.Match(line,"^([0-9A-F]{3})\\s+(.+)$");
    if(!match.Success)throw new IOException("Resposta sem cabeçalho CAN reconhecido: "+line);
    if(match.Groups[1].Value!="7E8")continue;
    string[] fields=Regex.Split(match.Groups[2].Value.Trim(),"\\s+");int start=0;
    if(fields[0].Length==1){int dlc=int.Parse(fields[0],NumberStyles.HexNumber);if(dlc>8||fields.Length-1!=dlc)throw new IOException("DLC inválido/truncado.");start=1;}
    List<byte> frame=new List<byte>();
    for(int i=start;i<fields.Length;i++){if(!Regex.IsMatch(fields[i],"^[0-9A-F]{2}$"))throw new IOException("Byte CAN inválido.");frame.Add(byte.Parse(fields[i],NumberStyles.HexNumber));}
    if(frame.Count<1||frame.Count>8)throw new IOException("Comprimento CAN inválido.");
    int kind=frame[0]>>4;
    if(kind==0){int n=frame[0]&15;if(n<1||n>7||frame.Count<n+1||assembly!=null)throw new IOException("Single frame incompleto/inválido.");complete.Add(frame.Skip(1).Take(n).ToArray());}
    else if(kind==1){if(frame.Count!=8||assembly!=null)throw new IOException("First frame inválido.");wanted=((frame[0]&15)<<8)|frame[1];if(wanted<8||wanted>4095)throw new IOException("Comprimento ISO-TP inválido.");assembly=frame.Skip(2).ToList();sequence=1;}
    else if(kind==2){if(assembly==null||(frame[0]&15)!=sequence)throw new IOException("Sequência ISO-TP inválida.");int remain=wanted-assembly.Count;if(frame.Count-1<Math.Min(7,remain))throw new IOException("Consecutive frame truncado.");assembly.AddRange(frame.Skip(1).Take(Math.Min(7,remain)));sequence=(sequence+1)&15;if(assembly.Count>=wanted){complete.Add(assembly.Take(wanted).ToArray());assembly=null;}}
    else if(kind!=3)throw new IOException("Tipo ISO-TP inesperado.");
   }
   if(assembly!=null)throw new IOException("Resposta ISO-TP incompleta.");
   if(complete.Count==0)throw new IOException("Nenhuma resposta da PCM7E8.");
   byte[][] positive=complete.Where(x=>x.Length==0||x[0]!=0x7f).ToArray();
   if(positive.Length>1)throw new IOException("Mais de uma resposta positiva; amostra ambígua.");
   if(positive.Length==1){if(complete.Any(x=>x.Length>=3&&x[0]==0x7f&&x[2]!=0x78))throw new IOException("Respostas contraditórias.");return positive[0];}
   return complete[complete.Count-1];
  }
  static void Require(byte[] p,int service,int size){
   if(p!=null&&p.Length>=3&&p[0]==0x7f)throw new DiagnosticRefusalException(p[1],p[2]);
   if(p==null||p.Length!=size||p[0]!=service)throw new IOException("Serviço ou tamanho da resposta incompatível.");
  }
  public static double FloatMemory(byte[] p,double min,double max){Require(p,0x63,5);byte[] a=p.Skip(1).ToArray();if(BitConverter.IsLittleEndian)Array.Reverse(a);double v=BitConverter.ToSingle(a,0);if(double.IsNaN(v)||double.IsInfinity(v)||v<min||v>max)throw new IOException("Valor RAM fora da faixa do perfil9M55AD.");return v;}
  public static uint UIntMemory(byte[] p,uint max){Require(p,0x63,5);uint v=((uint)p[1]<<24)|((uint)p[2]<<16)|((uint)p[3]<<8)|p[4];if(v>max)throw new IOException("Inteiro RAM fora da faixa do perfil.");return v;}
  public static double[] Cylinders(byte[] p){Require(p,0x63,17);double[] v=new double[4];for(int i=0;i<4;i++){byte[] q=new[]{(byte)0x63,p[1+4*i],p[2+4*i],p[3+4*i],p[4+4*i]};v[i]=UIntMemory(q,100000)/1000.0;}return v;}
  public static double Obd(byte[] p,int pid){Require(p,0x41,pid==12||pid==3?4:3);if(p[1]!=pid)throw new IOException("PID de resposta diferente do solicitado.");if(pid==12)return(p[2]*256+p[3])/4.0;if(pid==14)return p[2]/2.0-64;if(pid==17||pid==4)return p[2]*100.0/255.0;if(pid==5||pid==15)return p[2]-40;if(pid==6||pid==7)return(p[2]-128)*100.0/128.0;return p[2];}
  public static double NativeScaled(byte[] p,int did,int bytes,double divisor){if(bytes!=1&&bytes!=2)throw new ArgumentException("Formato nativo não previsto.");Require(p,0x62,3+bytes);if(((p[1]<<8)|p[2])!=did)throw new IOException("DID de resposta diferente do solicitado.");if(divisor<=0||double.IsNaN(divisor)||double.IsInfinity(divisor))throw new ArgumentException("Escala inválida.");return(bytes==1?p[3]:((p[3]<<8)|p[4]))/divisor;}
  public static double CompoundTrim(double stft,double ltft){if(double.IsNaN(stft)||double.IsNaN(ltft)||double.IsInfinity(stft)||double.IsInfinity(ltft))throw new ArgumentException("Correção inválida.");return((1+stft/100.0)*(1+ltft/100.0)-1)*100;}
  public static void RequireRpmSupport(byte[] p){Require(p,0x41,6);if(p[1]!=0)throw new IOException("Resposta não corresponde à consulta0100.");uint bitmap=((uint)p[2]<<24)|((uint)p[3]<<16)|((uint)p[4]<<8)|p[5];if((bitmap&0x00100000)==0)throw new IOException("PCM7E8 não anunciou suporte ao PID0C(RPM).");}
  public static string ProtocolNumber(string raw,string expected){string[] lines=raw.Replace('>',' ').Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).Where(x=>x.Length>0&&x!="ATDPN").ToArray();if(lines.Length!=1||!Regex.IsMatch(lines[0],"^A?[68]$")||lines[0].TrimStart('A')!=expected)throw new IOException("ATDPN não confirmou o protocolo CAN11bit esperado: "+expected+".");return lines[0].TrimStart('A');}
 }
 public class ConnectOptions {
  public string Port="";public int Baud=0;public int IntervalMs=500;public bool ProfileConfirmed=false,Demo=false,Cylinders=false,Context=false;
  public void Validate(){if(!ProfileConfirmed)throw new ArgumentException("Confirme o perfil9M55AD e a chave HS-CAN.");if(!Demo&&!Regex.IsMatch(Port??"","^COM[1-9][0-9]{0,3}$",RegexOptions.IgnoreCase))throw new ArgumentException("Selecione uma porta COM válida.");if(!new[]{0,9600,38400,57600,115200}.Contains(Baud))throw new ArgumentException("Baud rate não suportado.");if(IntervalMs<250||IntervalMs>10000)throw new ArgumentException("Intervalo deve ficar entre250 e10000ms.");}
 }
 public interface IElmChannel:IDisposable {void Open();void WriteLine(string command);string ReadChunk();}
 public sealed class SerialChannel:IElmChannel {
  SerialPort port;
  public SerialChannel(string name,int baud){port=new SerialPort(name,baud,Parity.None,8,StopBits.One);port.Handshake=Handshake.None;port.DtrEnable=false;port.RtsEnable=false;port.WriteTimeout=600;port.ReadTimeout=200;port.NewLine="\r";}
  public void Open(){port.Open();port.DiscardInBuffer();port.DiscardOutBuffer();}
  public void WriteLine(string s){port.Write(s+"\r");}
  public string ReadChunk(){return port.ReadExisting();}
  public void Dispose(){if(port!=null){try{port.Close();}finally{port.Dispose();port=null;}}}
 }
 public sealed class DemoChannel:IElmChannel {
  string queued="",protocol="6";readonly bool deny;Stopwatch timer=Stopwatch.StartNew();
  public DemoChannel():this(false){}public DemoChannel(bool denied){deny=denied;}
  public void Open(){}public void Dispose(){}
  static byte[] F(float v){byte[] a=BitConverter.GetBytes(v);if(BitConverter.IsLittleEndian)Array.Reverse(a);return a;}
  static string Frames(byte[] p){StringBuilder s=new StringBuilder();if(p.Length<8){s.Append("7E8 "+p.Length.ToString("X2")+" "+BitConverter.ToString(p).Replace('-',' '));}else{List<byte> f=new List<byte>{(byte)(0x10|(p.Length>>8)),(byte)p.Length};f.AddRange(p.Take(6));s.Append("7E8 "+BitConverter.ToString(f.ToArray()).Replace('-',' '));int seq=1;for(int i=6;i<p.Length;i+=7){s.Append("\r7E8 "+(0x20|seq).ToString("X2")+" "+BitConverter.ToString(p.Skip(i).Take(7).ToArray()).Replace('-',' '));seq=(seq+1)&15;}}return s+"\r>";}
  public void WriteLine(string command){ReadOnlyProfile.Guard(command);if(command=="ATI"){queued="ELM327 DEMONSTRACAO\r>";return;}if(command=="ATWS"){queued="ATWS\rELM327 DEMONSTRACAO\r>";return;}if(command=="ATTP6"||command=="ATTP8"){protocol=command.Substring(4);queued="OK\r>";return;}if(command=="ATDPN"){queued=protocol+"\r>";return;}if(command.StartsWith("AT")){queued="OK\r>";return;}if(command=="0100"){queued=Frames(new byte[]{0x41,0,0,0x10,0,0});return;}if(command.StartsWith("23")){if(deny){queued=Frames(new byte[]{0x7f,0x23,0x31});return;}List<byte> data=new List<byte>{0x63};double t=timer.Elapsed.TotalSeconds;if(command=="23003F9E040004")data.AddRange(F((float)(0.0028+0.0004*Math.Sin(t))));else if(command=="23003FAA700004")data.AddRange(F((float)(58+Math.Sin(t*.3))));else if(command=="23003F9DDC0004")data.AddRange(new byte[]{0,0,2,0xd0});else for(int i=0;i<4;i++)data.AddRange(new byte[]{0,0,0x0b,0xb8});queued=Frames(data.ToArray());return;}if(command.StartsWith("22")){int did=int.Parse(command.Substring(2),NumberStyles.HexNumber);int v=did==0x09d4?156:did==0x093c?42500:42000;List<byte> d=new List<byte>{0x62,(byte)(did>>8),(byte)did};if(did!=0x09d4)d.Add((byte)(v>>8));d.Add((byte)v);queued=Frames(d.ToArray());return;}int pid=int.Parse(command.Substring(2),NumberStyles.HexNumber);if(pid==12){int r=(int)((800+50*Math.Sin(timer.Elapsed.TotalSeconds))*4);queued=Frames(new byte[]{0x41,12,(byte)(r>>8),(byte)r});}else if(pid==3)queued=Frames(new byte[]{0x41,3,2,0});else{int n=pid==5?125:pid==15?75:pid==11?30:pid==14?168:pid==17?210:pid==6?144:pid==7?136:pid==4?64:128;queued=Frames(new byte[]{0x41,(byte)pid,(byte)n});}}
  public string ReadChunk(){string x=queued;queued="";return x;}
 }
 public sealed class Reading {public double? value;public string unit,utc,status;public Reading Copy(){return new Reading{value=value,unit=unit,utc=utc,status=status};}}
 public sealed class MonitorEngine {
  readonly object gate=new object();readonly string folder;readonly Func<string,int,IElmChannel> factory;readonly ManualResetEvent cancel=new ManualResetEvent(false);
  readonly Dictionary<string,Reading> values=new Dictionary<string,Reading>();readonly List<string> errors=new List<string>();Thread worker;IElmChannel channel;StreamWriter csv,raw;ConnectOptions options;
  string state="idle",message="Selecione a porta COM. Adaptador em HS-CAN; FORScan fechado.",adapter="",logPath="",rawPath="",mode="REAL",ramStatus="Leitura RAM ainda não testada.";int sequence;double hz,cycleMs;bool validated,ramAvailable,ramDisabled,transportReady,adapterConnected;string connectionStage="idle",protocol="",candidateProtocol="";Stopwatch session;
  public MonitorEngine(string directory):this(directory,delegate(string p,int b){return new SerialChannel(p,b);}){}
  public MonitorEngine(string directory,Func<string,int,IElmChannel> make){folder=Path.GetFullPath(directory);Directory.CreateDirectory(folder);factory=make;}
  public string State {get{lock(gate)return state;}}public int Sequence{get{lock(gate)return sequence;}}public string LogPath{get{lock(gate)return logPath;}}public string RawLogPath{get{lock(gate)return rawPath;}}
  static string Utc(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}
  void StateSet(string s,string text){lock(gate){state=s;message=text;}}
  public void Start(ConnectOptions o){o.Validate();lock(gate){if(worker!=null&&worker.IsAlive)throw new InvalidOperationException("Já existe uma conexão. Pare antes de reconectar.");options=o;values.Clear();errors.Clear();sequence=0;hz=cycleMs=0;validated=false;ramAvailable=ramDisabled=transportReady=adapterConnected=false;connectionStage="adapter";protocol=candidateProtocol="";ramStatus="Leitura RAM ainda não testada.";adapter="";mode=o.Demo?"DEMO":"REAL";state="connecting";message=o.Demo?"Iniciando demonstração, sem adaptador.":"Conectando ao ELM327; nenhuma gravação será enviada.";cancel.Reset();worker=new Thread(Run);worker.IsBackground=true;worker.Name="9M55AD read-only";worker.Start();}}
  public void Stop(){cancel.Set();Thread t;lock(gate)t=worker;if(t!=null&&t.IsAlive)t.Join(1800);lock(gate){if(state!="error"){state="stopped";message="Captura parada. Os valores exibidos são históricos.";}}}
  public string SnapshotJson(){lock(gate){Dictionary<string,Reading> copy=new Dictionary<string,Reading>();foreach(var x in values)copy[x.Key]=x.Value.Copy();return new JavaScriptSerializer().Serialize(new{state=state,message=message,adapter=adapter,sequence=sequence,cycleHz=hz,cycleMs=cycleMs,unifiedCycleMs=cycleMs,intervalMs=options==null?500:options.IntervalMs,ramAvailable=ramAvailable,ramStatus=ramStatus,mode=mode,connectionStage=connectionStage,protocol=protocol,protocolName=protocol=="6"?"CAN11bit500kbps":protocol=="8"?"CAN11bit250kbps":"Não confirmado",candidateProtocol=candidateProtocol,adapterConnected=adapterConnected&&!optionsDemo(),obdValidated=transportReady&&!optionsDemo(),liveValidated=validated&&!optionsDemo(),logPath=logPath,rawLogPath=rawPath,values=copy,errors=errors.ToArray()});}}
  bool optionsDemo(){return options!=null&&options.Demo;}
  void Put(string id,double number,string unit){lock(gate)values[id]=new Reading{value=number,unit=unit,utc=Utc(),status="ok"};}
  void Bad(string id,string why){lock(gate){values[id]=new Reading{value=null,unit="",utc=Utc(),status="error"};errors.Add(why);if(errors.Count>20)errors.RemoveAt(0);}}
  void Missing(string id,string status){lock(gate)values[id]=new Reading{value=null,unit="",utc=Utc(),status=status};}
  void DisableRam(Exception error){lock(gate){ramDisabled=true;ramAvailable=false;ramStatus="RAM indisponível: "+error.Message+" A captura de PIDs continua.";errors.Add(ramStatus);foreach(string id in new[]{"pulse_base_ms","offset_ms","pressure_model_psi","pressure_model_bar"})Missing(id,"unsupported");if(options.Cylinders)for(int i=1;i<=4;i++)Missing("cyl"+i+"_ms","unsupported");}}
  void ReadRam(){if(ramDisabled)return;try{ReadValue("pulse_base_ms",delegate{Put("pulse_base_ms",ElmCodec.FloatMemory(Request("23003F9E040004"),0,.1)*1000,"ms");});ReadValue("offset_ms",delegate{Put("offset_ms",ElmCodec.UIntMemory(Request("23003F9DDC0004"),2040)/1000.0,"ms");});ReadValue("pressure_model_psi",delegate{double psi=ElmCodec.FloatMemory(Request("23003FAA700004"),0,128);Put("pressure_model_psi",psi,"psi inferido");Put("pressure_model_bar",psi*0.0689475729318,"bar inferido");});if(options.Cylinders){ReadValue("cyl1_ms",delegate{double[] v=ElmCodec.Cylinders(Request("23003F89840010"));for(int i=0;i<4;i++)Put("cyl"+(i+1)+"_ms",v[i],"ms");});}lock(gate){ramAvailable=new[]{"pulse_base_ms","offset_ms","pressure_model_psi"}.All(id=>values.ContainsKey(id)&&values[id].status=="ok");ramStatus=!ramAvailable?"Uma leitura RAM não respondeu neste ciclo; haverá nova tentativa.":options.Demo?"RAM simulada; não validada no veículo.":"Leituras RAM recebidas; pressão permanece estimada.";}}catch(ElmNoDataException){throw;}catch(DiagnosticRefusalException e){if(e.Service==0x23&&new byte[]{0x11,0x12,0x31,0x33}.Contains(e.Code))DisableRam(e);else throw;}}
  static readonly string[] UnifiedIds={"spark_deg","app_pct","throttle_pct","etc_actual_deg","etc_desired_deg","stft_pct","ltft_pct","fuelsys1_code"};
  static readonly string[] UnifiedCommands={"010E","2209D4","0111","22093C","22091A","0106","0107","0103"};
  static readonly string[] UnifiedUnits={"°","% APP interno","% TP absoluto","° inferido","° inferido","%","%","flags OBD"};
  void ReadUnified(HashSet<string> unavailable){Dictionary<string,double> fresh=new Dictionary<string,double>();foreach(int k in new[]{1,3,4,0,2,5,6,7}){string id=UnifiedIds[k],cmd=UnifiedCommands[k];if(unavailable.Contains(id))continue;try{byte[] p=Request(cmd);double v;if(cmd.StartsWith("22")){int did=int.Parse(cmd.Substring(2),NumberStyles.HexNumber);v=ElmCodec.NativeScaled(p,did,did==0x09d4?1:2,did==0x09d4?2:512);}else v=ElmCodec.Obd(p,int.Parse(cmd.Substring(2),NumberStyles.HexNumber));fresh[id]=v;Put(id,v,UnifiedUnits[k]);if(cmd=="0103")Put("fuelsys2_code",p[3],"flags OBD raw");}catch(ElmNoDataException e){Bad(id,"PID indisponível: "+e.Message);Missing(id,"no_data");if(cmd=="0103")Missing("fuelsys2_code","no_data");}}
   if(!fresh.ContainsKey("stft_pct")||!fresh.ContainsKey("ltft_pct")||!fresh.ContainsKey("fuelsys1_code"))Missing("total_trim_pct","unavailable");else if(fresh["fuelsys1_code"]!=2)Missing("total_trim_pct","inactive_ol");else Put("total_trim_pct",ElmCodec.CompoundTrim(fresh["stft_pct"],fresh["ltft_pct"]),"% derivado CL");
  }
  string Exchange(string command,int timeout){ReadOnlyProfile.Guard(command);if(cancel.WaitOne(0))throw new OperationCanceledException();Stopwatch sw=Stopwatch.StartNew();StringBuilder buffer=new StringBuilder();try{channel.WriteLine(command);while(sw.ElapsedMilliseconds<timeout){if(cancel.WaitOne(0))throw new OperationCanceledException();buffer.Append(channel.ReadChunk());if(buffer.Length>65536)throw new IOException("Resposta excessiva do adaptador.");int p=buffer.ToString().IndexOf('>');if(p>=0){string answer=buffer.ToString(0,p+1);LogRaw(command,answer,sw.Elapsed.TotalMilliseconds,null);return answer;}Thread.Sleep(10);}throw new TimeoutException("Timeout aguardando prompt do ELM327 ("+command+").");}catch(Exception e){LogRaw(command,buffer.ToString(),sw.Elapsed.TotalMilliseconds,e.Message);throw;}}
  void LogRaw(string command,string response,double ms,string error){if(raw!=null)raw.WriteLine(new JavaScriptSerializer().Serialize(new{utc=Utc(),mode=mode,command=command,response=response,elapsed_ms=ms,error=error,stage=connectionStage,protocol=protocol,candidateProtocol=candidateProtocol}));}
  bool functionalObd;string requestHeader="";
  byte[] Request(string c){
   string header=c.StartsWith("01")&&functionalObd?"ATSH7DF":"ATSH7E0";
   if(requestHeader!=header){ConfirmAdapter(header);requestHeader=header;}
   return ElmCodec.Payload(Exchange(c,2200));
  }
  void Stage(string stage,string text){lock(gate){connectionStage=stage;message=text;}}
  void ConfirmAdapter(string command){
   string answer=Exchange(command,1800);
   string[] lines=answer.Replace('>',' ').Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).Where(x=>x.Length>0&&x!=command).ToArray();
   if(lines.Length!=1||lines[0]!="OK")throw new IOException("Adaptador USB não confirmou "+command+". O comando pode não ser implementado; não será usado protocolo permanente, atualização ou desbloqueio. "+answer.Replace('>',' ').Trim());
  }
  void ResetReceiveFilter(){
   string reply=Exchange("ATCRA",900);
   string[] lines=reply.Replace('>',' ').Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).Where(x=>x.Length>0&&x!="ATCRA").ToArray();
   if(lines.Length==1&&lines[0]=="?"){ConfirmAdapter("ATAR");return;}
   if(lines.Length!=1||lines[0]!="OK")throw new IOException("ELM não confirmou a limpeza do filtro CAN: "+reply);
  }
  void ConfigureAdapter(){
   Stage("configuring","Interface USB identificada. Preparando configuração volátil do ELM, sem atualizar firmware.");
   string banner=Exchange("ATWS",1800);if(!Regex.IsMatch(banner,"ELM|STN|OBDLINK",RegexOptions.IgnoreCase))throw new IOException("Interface USB não confirmou reinício volátil ATWS. Nenhuma atualização foi tentada.");
   foreach(string cmd in ReadOnlyProfile.Init)ConfirmAdapter(cmd);
  }
  void Initialize(){
   missingRpm=0;functionalObd=false;requestHeader="";
   int[] rates=options.Baud==0?new[]{38400,115200,9600,57600}:new[]{options.Baud};Exception last=null;
   Stage("adapter","Identificando a interface USB/serial; isso ainda não confirma comunicação com o carro.");
   foreach(int baud in rates){
    if(cancel.WaitOne(0))throw new OperationCanceledException();
    try{channel=options.Demo?(IElmChannel)new DemoChannel():factory(options.Port,baud);channel.Open();string id=Exchange("ATI",900);if(!Regex.IsMatch(id,"ELM|STN|OBDLINK",RegexOptions.IgnoreCase))throw new IOException("Resposta não identifica um ELM/STN.");lock(gate){adapter=id.Replace('>',' ').Trim()+" · "+baud+" baud";adapterConnected=true;}last=null;break;}
    catch(OperationCanceledException){throw;}
    catch(Exception e){last=e;if(channel!=null){channel.Dispose();channel=null;}}
   }
   if(last!=null)throw new IOException("Não foi possível identificar a interface USB. Confira COM, driver, baud e FORScan fechado. "+last.Message);
   ConfigureAdapter();List<string> attempts=new List<string>();
   foreach(string number in new[]{"6","8"}){
    lock(gate)candidateProtocol=number;
    Stage("discovering","USB/ELM respondeu; procurando PCM7E8 por OBD0100 funcional, CAN11bit "+(number=="6"?"500":"250")+"kbps. Ignição e alimentação OBD são necessárias.");
    ConfirmAdapter("ATTP"+number);ResetReceiveFilter();ConfirmAdapter("ATSH7DF");
    try{ElmCodec.RequireRpmSupport(ElmCodec.Payload(Exchange("0100",8000)));}
    catch(OperationCanceledException){throw;}
    catch(TimeoutException e){throw new IOException("USB/ELM respondeu, mas a descoberta não terminou no prazo. Captura não iniciada para evitar respostas fora de sequência. Confira alimentação OBD, ignição, HS-CAN e cabo; não atualize o firmware. "+e.Message);}
    catch(IOException e){string why="CAN11bit"+(number=="6"?"500":"250")+": "+e.Message;attempts.Add(why);lock(gate)errors.Add(why);continue;}
    // Once the PCM answers on this bus, confirmation failures stop rather than changing its bit rate.
    string detected=ElmCodec.ProtocolNumber(Exchange("ATDPN",900),number);
    Stage("confirming","PCM7E8 respondeu à descoberta. Confirmando RPM com endereçamento físico7E0; captura ainda não iniciada.");
    ConfirmAdapter("ATSH7E0");requestHeader="ATSH7E0";ConfirmAdapter("ATCRA7E8");double rpm;
    try{rpm=ElmCodec.Obd(Request("010C"),12);}
    catch(DiagnosticRefusalException e){
     if(e.Service!=1||(e.Code!=0x11&&e.Code!=0x12))throw;
     Stage("confirming","PCM respondeu à descoberta, mas recusou OBD físico. Confirmando RPM por OBD funcional7DF; captura ainda bloqueada.");
     functionalObd=true;rpm=ElmCodec.Obd(Request("010C"),12);
    }
    catch(OperationCanceledException){throw;}
    catch(Exception e){throw new IOException("USB/ELM e descoberta responderam, mas a PCM7E8 não confirmou RPM física. Captura não iniciada; confira ignição e alimentação OBD. "+e.Message,e);}
    lock(gate){protocol=detected;transportReady=true;}Put("rpm",rpm,"rpm");Stage("ready",options.Demo?"Transporte simulado pronto; nenhum carro conectado.":"Transporte OBD confirmado pela PCM7E8. Iniciando leituras do perfil9M55AD.");return;

   }
   throw new IOException("USB/ELM respondeu, mas a PCM7E8 não foi validada em CAN11bit500/250. Fora do carro ou sem ignição/alimentação OBD, isso é esperado. Confira chave HS, cabo e alimentação; a etiqueta25K80/FTDI não comprova compatibilidade. Nenhum dado do carro foi aceito. "+string.Join(" | ",attempts));
  }
  int missingRpm;
  void ReadValue(string id,Action a){try{a();if(id=="rpm")missingRpm=0;}catch(ElmNoDataException e){Missing(id,"no_data");if(id=="pressure_model_psi")Missing("pressure_model_bar","no_data");if(id=="cyl1_ms")for(int channelNumber=2;channelNumber<=4;channelNumber++)Missing("cyl"+channelNumber+"_ms","no_data");if(id=="rpm"&&++missingRpm>=3)throw new IOException("PCM sem RPM em três ciclos consecutivos. Captura encerrada; reconecte após ligar a ignição.",e);}catch(OperationCanceledException){throw;}catch(Exception e){Bad(id,e.Message);throw;}}
  void Run(){
   try{
    string tag=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+"_"+mode;
    lock(gate){logPath=Path.Combine(folder,tag+".csv");rawPath=Path.Combine(folder,tag+"_bruto.jsonl");}
    csv=new StreamWriter(logPath,false,new UTF8Encoding(true));raw=new StreamWriter(rawPath,false,new UTF8Encoding(false));csv.AutoFlush=true;raw.AutoFlush=true;session=Stopwatch.StartNew();WriteHeader();Initialize();
    string[] contextIds={"ect_c","iat_c","map_kpa","load_pct"};int[] contextPids={5,15,11,4};string[] units={"°C","°C","kPa","%"};int contextIndex=0;HashSet<string> unavailable=new HashSet<string>();
    while(!cancel.WaitOne(0)){
     Stopwatch round=Stopwatch.StartNew();
     ReadRam();
     ReadUnified(unavailable);
     ReadValue("rpm",delegate{Put("rpm",ElmCodec.Obd(Request("010C"),12),"rpm");});
     if(options.Context){int k=contextIndex++%contextIds.Length;if(!unavailable.Contains(contextIds[k]))try{Put(contextIds[k],ElmCodec.Obd(Request("01"+contextPids[k].ToString("X2")),contextPids[k]),units[k]);}catch(ElmNoDataException e){Bad(contextIds[k],"Contexto sem resposta neste ciclo: "+e.Message);Missing(contextIds[k],"no_data");}}
     lock(gate){cycleMs=round.Elapsed.TotalMilliseconds;sequence++;validated=!options.Demo;state=options.Demo?"demo":"running";message=options.Demo?"DEMONSTRAÇÃO: dados simulados; nenhum adaptador conectado.":missingRpm>0?"Sem RPM neste ciclo; aguardando recuperação. Lacuna registrada sem reutilizar o valor anterior.":ramDisabled?"Captura unificada de PIDs ativa; RAM/pulso/pressão indisponíveis nesta sessão.":"Captura unificada ativa. Pressão é estimada; pulso base não inclui offset.";}
     WriteRow();int rest=Math.Max(0,options.IntervalMs-(int)round.ElapsedMilliseconds);if(cancel.WaitOne(rest))break;lock(gate)hz=1000.0/Math.Max(round.Elapsed.TotalMilliseconds,1);
    }
   }catch(OperationCanceledException){lock(gate)connectionStage=transportReady?"stopped":"cancelled";}catch(Exception e){lock(gate){connectionStage="failed";state="error";message=e.Message;errors.Add(e.Message);validated=false;ramAvailable=false;foreach(Reading v in values.Values)v.status="error";}try{if(transportReady)WriteRow();}catch{}}
   finally{if(channel!=null){try{channel.Dispose();}catch{}channel=null;}if(csv!=null){csv.Dispose();csv=null;}if(raw!=null){raw.Dispose();raw=null;}lock(gate){adapterConnected=false;transportReady=false;validated=false;if(state!="error"){if(connectionStage!="cancelled")connectionStage="stopped";state="stopped";message="Captura encerrada. Valores históricos; arquivos salvos.";}}}
  }
  static readonly string[] Columns={"pulse_base_ms","offset_ms","pressure_model_psi","pressure_model_bar","rpm","spark_deg","app_pct","throttle_pct","etc_actual_deg","etc_desired_deg","stft_pct","ltft_pct","total_trim_pct","fuelsys1_code","fuelsys2_code","cyl1_ms","cyl2_ms","cyl3_ms","cyl4_ms","ect_c","iat_c","map_kpa","load_pct"};
  static string Quote(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
  void WriteHeader(){List<string> h=new List<string>{"utc","mode","elapsed_s","sequence","cycle_ms","state","target_interval_ms","cycle_hz","ram_available","ram_status"};foreach(string id in Columns){h.Add(id);h.Add(id+"_utc");h.Add(id+"_status");h.Add(id+"_age_ms");}csv.WriteLine(string.Join(",",h));}
  void WriteRow(){if(csv==null||session==null)return;lock(gate){DateTime now=DateTime.UtcNow;List<string> row=new List<string>{Quote(now.ToString("o",CultureInfo.InvariantCulture)),Quote(mode),session.Elapsed.TotalSeconds.ToString("F3",CultureInfo.InvariantCulture),sequence.ToString(CultureInfo.InvariantCulture),cycleMs.ToString("F2",CultureInfo.InvariantCulture),Quote(state),options.IntervalMs.ToString(CultureInfo.InvariantCulture),hz.ToString("F4",CultureInfo.InvariantCulture),ramAvailable?"true":"false",Quote(ramStatus)};foreach(string id in Columns){Reading v;if(values.TryGetValue(id,out v)){row.Add(v.value.HasValue?v.value.Value.ToString("G12",CultureInfo.InvariantCulture):"");row.Add(Quote(v.utc));row.Add(Quote(v.status));DateTime stamp;if(DateTime.TryParse(v.utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out stamp))row.Add(Math.Max(0,(now-stamp.ToUniversalTime()).TotalMilliseconds).ToString("F1",CultureInfo.InvariantCulture));else row.Add("");}else{row.Add("");row.Add("");row.Add(Quote("not_requested"));row.Add("");}}csv.WriteLine(string.Join(",",row));}}
 }
}
