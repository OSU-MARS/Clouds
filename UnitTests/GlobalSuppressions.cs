// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "MSTEST0063:Test classes should have valid constructors", Justification = "MSTEST0063 bug", Scope = "type", Target = "~T:Mars.Clouds.UnitTests.CloudTest")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.VrtTests")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.SimdTests")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.LasTests")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.GridTests")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.ExtensionTests")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods", Justification = "readability", Scope = "member", Target = "~T:Mars.Clouds.UnitTests.DiskTests")]
[assembly: SuppressMessage("Performance", "CA1855:Prefer 'Clear' over 'Fill'", Justification = "clarity, reliability", Scope = "member", Target = "~M:Mars.Clouds.UnitTests.GridTests.NeighborhoodSlices")]
[assembly: SuppressMessage("Design", "MSTEST0016:Test class should have test method", Justification = "MSTEST0016 bug", Scope = "type", Target = "~T:Mars.Clouds.UnitTests.CloudTest")]
