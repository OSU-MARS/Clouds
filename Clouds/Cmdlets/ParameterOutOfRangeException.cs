using System;

namespace Mars.Clouds.Cmdlets
{
    internal class ParameterOutOfRangeException : ArgumentOutOfRangeException
    {
        public ParameterOutOfRangeException(string parameterName, string message)
            : base(parameterName, message) 
        {
        }
    }
}
