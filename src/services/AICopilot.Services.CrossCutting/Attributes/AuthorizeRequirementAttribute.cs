using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Services.CrossCutting.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class AuthorizeRequirementAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ResourceAuthorizationOwnerAttribute(Type ownerType) : Attribute
{
    public Type OwnerType { get; } = ownerType;
}
