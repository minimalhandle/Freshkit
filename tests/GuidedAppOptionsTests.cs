using System;
using System.IO;
using System.Reflection;
using System.Linq;
namespace Freshkit {
 internal static class GuidedAppOptionsTests {
 static object Call(string type,string name,params object[] args){var method=typeof(ListFile).Assembly.GetType("Freshkit."+type).GetMethod(name,BindingFlags.Public|BindingFlags.Static);if(method==null)throw new Exception("Missing guided behavior: "+name);return method.Invoke(null,args);}
 public static void Run(Action<bool,string> check){
 string git=(string)Call("AppOptions","GuidedArguments","choco","git.install","/SChannel /NoGitLfs", "", new[]{"NoShellIntegration"});
 check(git=="/SChannel /NoShellIntegration","Component choices replace documented flags and preserve custom arguments");
 check((string)Call("AppOptions","GuidedArguments","choco","git.install","/Custom=\"keep /NoGitLfs here\"", "",new string[0])=="/Custom=\"keep /NoGitLfs here\"","Guided edits preserve known-looking switches inside quoted custom values");
 check((string)Call("AppOptions","GuidedArguments","choco","git.install","/Custom=\"unfinished", "",new string[0])=="/Custom=\"unfinished","Guided edits do not discard unfinished advanced text");
 string firefox=(string)Call("AppOptions","GuidedArguments","choco","firefox","/l:en-US /InstallDir=C:\\Apps", "de",new[]{"NoDesktopShortcut"});
 check(firefox=="/InstallDir=C:\\Apps /l:de /NoDesktopShortcut","Firefox language and component choices merge into package parameters");
 check((string)Call("AppOptions","LanguagePackage","Mozilla.Firefox.en-GB","en-US")=="Mozilla.Firefox","US English maps to the verified unsuffixed winget package");
 check((string)Call("AppOptions","GuidedArguments","winget","Mozilla.Firefox.en-GB","/S /DesktopShortcut=false","fr",new string[0])=="/S /DesktopShortcut=false","Localized winget package selection preserves advanced installer overrides");
 string catalog="::BEGIN_PACKAGES\r\n::END_PACKAGES\r\n::BEGIN_WINGET\r\n::GROUP Browsers\r\n::DISABLED Mozilla.Firefox.en-GB|Firefox|/S\r\n::END_WINGET\r\n::BEGIN_PROFILES\r\n::PROFILE Test|apps=winget:Mozilla.Firefox.en-GB,steam\r\n::PROFILE Spaced| Apps = winget:Mozilla.Firefox.en-GB\r\n::END_PROFILES\r\n";
 Engine.SessionList=catalog;
 Call("ListFile","SetPackageOptions","winget","Mozilla.Firefox.en-GB","Mozilla.Firefox.fr","/S /DesktopShortcut=false");
 var list=ListFile.Parse(Engine.SessionList);
 check(list.Packages[0].Id=="Mozilla.Firefox.fr"&&!list.Packages[0].Enabled&&list.Packages[0].Category=="Browsers","Language package edit preserves category and default disabled state");
 check(list.Saved[0].Apps[0]=="winget:Mozilla.Firefox.fr"&&!list.Saved[0].HasTweaks,"Language package edit remaps profile identity without changing profile semantics");
 check(list.Saved[1].Apps[0]=="winget:Mozilla.Firefox.fr","Profile key migration respects whitespace accepted by the profile parser");
 var recipe=new SetupRecipe{ListText=Engine.SessionList};recipe.Apps.Add("winget:Mozilla.Firefox.fr");check(SetupRecipe.Parse(recipe.Serialize(),catalog).Apps[0]=="winget:Mozilla.Firefox.fr","Localized package and arguments survive recipe round trip");
 Engine.SessionList=catalog.Replace("::END_WINGET","::WINGET Mozilla.Firefox.fr|French Firefox|\r\n::END_WINGET");string before=Engine.SessionList;
 try{Call("ListFile","SetPackageOptions","winget","Mozilla.Firefox.en-GB","Mozilla.Firefox.fr","");throw new Exception("Duplicate language target accepted");}catch(TargetInvocationException ex){check(ex.InnerException is InvalidDataException,"Duplicate language target is rejected");}check(Engine.SessionList==before,"Rejected language switch leaves catalog and profiles untouched");
 try{Call("ListFile","SetPackageOptions","winget","Mozilla.Firefox.en-GB","Other.Package","");throw new Exception("Arbitrary identity accepted");}catch(TargetInvocationException ex){check(ex.InnerException is InvalidDataException,"Guided language editing cannot change arbitrary package identity");}
 Engine.SessionList=null;
 }
 }
}




