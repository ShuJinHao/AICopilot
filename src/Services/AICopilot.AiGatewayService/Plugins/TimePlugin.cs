using AICopilot.AgentPlugin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AICopilot.AiGatewayService.Plugins;

public class TimePlugin : AgentPluginBase
{
    [Description("获取当前系统时间")]
    public string GetCurrentTime()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}