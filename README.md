### Overview
A research codebase with an ad hoc collection of tools for working with remotely sensed data, primarily point clouds.

### Dependencies
Clouds is a .NET 7.0 assembly which includes C# cmdlets for PowerShell Core. It therefore makes use of both the System.Management.Automation
nuget package and the system's PowerShell Core installation, creating a requirement the PowerShell Core version be the same or newer than 
the nuget's. If Visual Studio Code is used for PowerShell Core execution then corresponding updates to Visual Studio Code and its PowerShell 
extension are required.

Clouds is developed using current or near-current versions of [Visual Studio Community](https://visualstudio.microsoft.com/downloads/) 
edition. In principle it can be ported to any .NET supported platform with minimal effort. Clouds is only tested on Windows 10.