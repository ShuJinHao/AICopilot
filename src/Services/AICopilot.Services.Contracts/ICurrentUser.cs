using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Services.Contracts;

public interface ICurrentUser
{
    string? Id { get; }

    string? UserName { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }
}