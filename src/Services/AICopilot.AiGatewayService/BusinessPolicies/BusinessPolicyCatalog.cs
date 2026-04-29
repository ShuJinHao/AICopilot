namespace AICopilot.AiGatewayService.BusinessPolicies;

public interface IBusinessPolicyCatalog
{
    IReadOnlyCollection<BusinessPolicyDescriptor> GetAll();

    bool TryGet(string intent, out BusinessPolicyDescriptor descriptor);
}

public sealed record BusinessPolicyDescriptor(
    string Intent,
    string Description,
    IReadOnlyList<string> ExampleQuestions,
    string Conclusion,
    IReadOnlyList<string> ApplicableConditions,
    IReadOnlyList<string> RestrictedBoundaries);

public sealed class BusinessPolicyCatalog : IBusinessPolicyCatalog
{
    private readonly IReadOnlyDictionary<string, BusinessPolicyDescriptor> _descriptors =
        BuildDescriptors().ToDictionary(item => item.Intent, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<BusinessPolicyDescriptor> GetAll()
    {
        return _descriptors.Values.OrderBy(item => item.Intent).ToArray();
    }

    public bool TryGet(string intent, out BusinessPolicyDescriptor descriptor)
    {
        return _descriptors.TryGetValue(intent, out descriptor!);
    }

    private static IReadOnlyList<BusinessPolicyDescriptor> BuildDescriptors()
    {
        return
        [
            new(
                "Policy.EmployeeAuthorization",
                "人员授权规则：客户端修改类操作必须同时满足设备分配和功能权限双重校验。",
                [
                    "员工要修改机台参数需要什么权限？",
                    "Can an operator change recipe settings without device assignment?"
                ],
                "客户端修改类操作必须同时满足设备分配和功能权限双重校验两个条件，缺一不可。",
                [
                    "适用于硬件参数修改、参数配方修改、配方参数修改。",
                    "人员可以先入职，后分配设备机台。"
                ],
                [
                    "不能只校验设备分配，不校验功能权限。",
                    "不能只校验功能权限，不校验设备分配。"
                ]),
            new(
                "Policy.DeviceRegistration",
                "设备注册规则：新设备注册是管理员专属业务，不是普通业务权限。",
                [
                    "谁可以注册新设备？",
                    "Can a normal user register a new device?"
                ],
                "新设备注册当前定义为绝对管理员操作，只有管理员可以注册，不属于普通权限放权范围。",
                [
                    "只有管理员可以发起新设备注册。",
                    "设备入厂建立正式主数据时适用。",
                    "注册成功后由云端生成唯一 ClientCode 作为寻址码。"
                ],
                [
                    "不能放宽成非管理员也可以注册新设备。",
                    "不能放宽成任何具备 Device.Create 权限的普通用户都可注册。",
                    "不能把设备寻址码改成后续可人工维护字段。"
                ]),
            new(
                "Policy.DeviceLifecycle",
                "设备生命周期规则：设备只允许改名，删除前必须校验历史依赖。",
                [
                    "设备能删除吗？",
                    "Can the device code be edited or can the device be hard deleted?"
                ],
                "设备只允许修改名称；寻址码不允许修改；删除为硬删除且必须先确认没有历史数据依赖。",
                [
                    "历史依赖至少包括配方、产能记录、设备日志、生产数据或过站数据。",
                    "只要存在任一历史依赖，就必须禁止删除设备。"
                ],
                [
                    "不能把设备删除改成软删除替代当前硬删除规则。",
                    "不能放宽设备删除前的数据依赖校验。"
                ]),
            new(
                "Policy.BootstrapIdentity",
                "bootstrap 与上传身份规则：客户端必须先通过 ClientCode 完成 bootstrap，再使用 DeviceId 上传归档。",
                [
                    "ClientCode 和 DeviceId 是什么关系？",
                    "Can the client upload production data directly by device name?"
                ],
                "客户端必须先凭 ClientCode 完成 bootstrap，云端返回正式 DeviceId，后续上传统一以 DeviceId 归档。",
                [
                    "适用于产能上传、日志上传、生产数据上传以及其他依赖正式设备身份的接口。",
                    "bootstrap 失败时只能继续尝试恢复身份，不能继续调用普通业务上传接口。"
                ],
                [
                    "不能绕过 bootstrap 直接凭设备名称上传数据。",
                    "不能把 ClientCode 直接当成正式归档主键，也不能用设备名称代替 DeviceId。"
                ]),
            new(
                "Policy.RecipeVersioning",
                "配方版本规则：配方归属于设备与工序，修改采用版本化管理而不是覆盖旧版本。",
                [
                    "配方修改是覆盖还是新建版本？",
                    "Does recipe editing overwrite the active version?"
                ],
                "配方归属于具体设备和工序；修改不是覆盖旧记录，而是版本化管理。",
                [
                    "初始版本从 V1.0 开始。",
                    "新版本创建后，旧活动版本要归档保留用于追溯。"
                ],
                [
                    "不能把配方逻辑改成原地更新当前版本。",
                    "不能删除旧版本的追溯价值。"
                ])
        ];
    }
}
