using System;

namespace Mars.Clouds.Cmdlets
{
    internal class ParameterOutOfRangeException(string parameterName, string message) : ArgumentOutOfRangeException(parameterName, message)
    {
    }
}
