using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: InternalsVisibleTo("EncodingChecker.Tests")]

// This assembly is Windows Forms and only runs on Windows. Setting this explicitly (normally
// implied automatically by the "net10.0-windows" TargetFramework via SDK-generated assembly info,
// which is disabled here since this handwritten file supplies the assembly attributes instead)
// avoids CA1416 platform-compatibility warnings on every Windows Forms API call in this assembly.
[assembly: SupportedOSPlatform("windows")]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("File Encoding Checker")]
[assembly: AssemblyDescription("GUI tool to check the encoding of a text file")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Jeevan James")]
[assembly: AssemblyProduct("File Encoding Checker")]
[assembly: AssemblyCopyright("Copyright © Jeevan James 2020")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("134e6b14-a7be-4ced-8332-3a2ca6023ee1")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values, or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("3.11.1.0")]
[assembly: AssemblyFileVersion("3.11.1.0")]
