using System;
using System.IO;
using System.Reflection;
using System.Linq;
namespace Freshkit {
 internal static class AppOptionsTests {
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static MethodInfo Method(string type,string method){var t=typeof(ListFile).Assembly.GetType("Freshkit."+type);var m=t==null?null:t.GetMethod(method,BindingFlags.Public|BindingFlags.Static);if(m==null)throw new Exception("Missing app options behavior: "+type+"."+method);return m;}
 static string Validate(string source,string id,string value){return (string)Method("AppOptions","Validate").Invoke(null,new object[]{source,id,value});}
 static void Set(string source,string id,string value){Method("ListFile","SetPackageParameters").Invoke(null,new object[]{source,id,value});}
 public static int Main(){try{
 Check(Validate("choco","firefox","/l:en-GB")==null,"Chocolatey accepts package language parameters");
 Check(Validate("github","owner/repo","ADDLOCAL=Core REMOVE=Extra INSTALLDIR=\"C:\\Program Files\\App\" /log \"C:\\Temp\\setup.log\"")==null,"MSI feature properties, quoted local paths and logging remain available");
 Check(Validate("winget","Mozilla.Firefox.en-GB","/S /DesktopShortcut=false")==null,"winget accepts installer override switches");
 foreach(string value in new[]{"x|y","x\ny","x\ty","x&calc","x;calc","`calc`","$(calc)","--source evil","\"--source\" evil","/S\u2028calc","--id Other.App","/i other.msi","--installer-url C:\\other.exe","https://evil.invalid/setup.exe","powershell -Command calc","\"/S"}) Check(Validate("winget","Mozilla.Firefox",value)!=null,"Rejects unsafe or misdirected arguments: "+value.Replace("\n","newline"));
 Check(AppOptions.Presets("choco","git.install").Count==2&&AppOptions.Presets("winget","git.install").Count==0,"Curated package presets stay tied to their source");
 Check(Validate("store","9NKSQGP7F2NH","/S")!=null,"Store installer overrides are unavailable");
 Check(Validate("winget","9NKSQGP7F2NH","")==null,"Store can retain empty defaults");
 string original="::BEGIN_PACKAGES\r\n::GROUP Browsers\r\n::DISABLED firefox|Firefox|/l:en-US\r\n::END_PACKAGES\r\n::BEGIN_WINGET\r\n::WINGET firefox|Other Firefox|\r\n::END_WINGET\r\n::BEGIN_GITHUB\r\n::GITHUB owner/repo|App|\r\n::END_GITHUB\r\n";
 Engine.SessionList=original;Set("choco","FIREFOX","/l:en-GB");
 var parsed=ListFile.Parse(Engine.SessionList);Check(parsed.Packages[0].Parameters=="/l:en-GB"&&!parsed.Packages[0].Enabled&&parsed.Packages[0].Category=="Browsers","Editing arguments preserves disabled state and category");
 Check(parsed.Packages[1].Parameters==""&&Engine.SessionList.Contains("Other Firefox"),"Editing arguments keeps other sources and display names");
 var recipe=new SetupRecipe{ListText=Engine.SessionList};var restored=SetupRecipe.Parse(recipe.Serialize(),original);Check(ListFile.Parse(restored.ListText).Packages[0].Parameters=="/l:en-GB","Recipe round trip retains session arguments");
 try { ListFile.Parse(original.Replace("/l:en-US","--source evil")); throw new Exception("Unsafe imported arguments accepted"); } catch(InvalidDataException) { Check(true,"Imported catalogs reject package identity overrides"); }
 string before=Engine.SessionList;try{Set("choco","firefox","/S|bad");throw new Exception("Expected invalid arguments rejection");}catch(TargetInvocationException ex){Check(ex.InnerException is InvalidDataException,"Setter rejects invalid arguments");}Check(Engine.SessionList==before,"Invalid edits leave the session untouched");
 Set("github","owner/repo","/S");Check(ListFile.Parse(Engine.SessionList).Packages[2].Parameters=="/S","GitHub arguments survive list reload");
 Set("choco","firefox","");Check(ListFile.Parse(Engine.SessionList).Packages[0].Parameters=="","Empty arguments restore package defaults");
 GuidedAppOptionsTests.Run(Check);
 Console.WriteLine(checks+" app options checks passed.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{Engine.SessionList=null;}}
 }
}




