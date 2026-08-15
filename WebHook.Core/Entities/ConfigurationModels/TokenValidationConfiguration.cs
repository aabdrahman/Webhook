using System;
using System.Collections.Generic;
using System.Text;

namespace WebHook.Core.Entities.ConfigurationModels;

public class TokenValidationConfiguration
{
    public double OtpExpirationAfterInSeconds { get; set; }
    public double OtpOperationTokenExpiresAFterInSceonds { get; set; }
}
