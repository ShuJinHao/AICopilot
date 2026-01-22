using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Services.Common.Exceptions;

public class ForbiddenException(string? message) : Exception(message);