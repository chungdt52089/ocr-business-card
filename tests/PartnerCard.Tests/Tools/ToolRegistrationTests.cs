using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Tools;

/// <summary>
/// T-11 — ranh giới của <c>tools/list</c> (SPEC mục 10.1 và 10.4): M-01, M-02, M-03, M-10.
///
/// Dựng danh sách tool bằng **đúng lời gọi** <c>WithToolsFromAssembly</c> mà Program.cs dùng, không
/// dựng lại bằng tay — thứ client nhìn thấy là thứ đăng ký sinh ra, không phải thứ ta nghĩ nó sinh ra.
/// </summary>
[Trait("Category", "Tool")]
public sealed partial class ToolRegistrationTests
{
    private static readonly string[] ExpectedTools =
        ["extract_business_card", "save_partner", "search_partners", "enrich_partner"];

    private static readonly Assembly WebAssembly = typeof(CardPipeline).Assembly;

    [Fact]
    public void M01_tools_list_tra_dung_4_tool_ten_snake_case()
    {
        var tools = RegisteredTools();

        tools.Select(tool => tool.Name).Should().BeEquivalentTo(ExpectedTools);
        tools.Should().AllSatisfy(tool => tool.Name.Should().MatchRegex("^[a-z]+(_[a-z]+)+$"));
    }

    [Fact]
    public void M02_moi_tool_co_mo_ta_tieng_Viet_khong_rong()
    {
        foreach (var tool in RegisteredTools())
        {
            tool.Description.Should().NotBeNullOrWhiteSpace(tool.Name);
            tool.Description.Should().MatchRegex(VietnameseLetter().ToString(), tool.Name);
        }
    }

    /// <summary>
    /// Kiểm hai phía: schema client nhận có <c>description</c> cho từng thuộc tính, **và** mỗi tham số
    /// trong code có <see cref="DescriptionAttribute"/>. Đếm thêm số thuộc tính bằng số tham số, để một
    /// tham số lỡ bị SDK coi là dịch vụ DI (và biến khỏi schema) không lọt qua im lặng.
    /// </summary>
    [Fact]
    public void M03_moi_tham_so_co_Description()
    {
        var tools = RegisteredTools().ToDictionary(tool => tool.Name);

        foreach (var (name, method) in ToolMethods())
        {
            var parameters = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .ToList();

            foreach (var parameter in parameters)
            {
                var attribute = parameter.GetCustomAttribute<DescriptionAttribute>();
                attribute.Should().NotBeNull($"{name}.{parameter.Name} phải có [Description]");
                attribute!.Description.Should().NotBeNullOrWhiteSpace($"{name}.{parameter.Name}");
            }

            var properties = tools[name].InputSchema.GetProperty("properties").EnumerateObject().ToList();
            properties.Should().HaveCount(parameters.Count, $"mọi tham số của {name} phải có mặt trong schema");

            foreach (var property in properties)
            {
                property.Value.TryGetProperty("description", out var description)
                    .Should().BeTrue($"{name}.{property.Name} thiếu description trong schema");
                description.GetString().Should().NotBeNullOrWhiteSpace($"{name}.{property.Name}");
            }
        }
    }

    /// <summary>
    /// Ca bảo vệ thiết kế (TEST-SPEC mục 7): gắn <c>[McpServerTool]</c> lên phương thức xoá thì ca này
    /// phải đỏ — kể cả khi lớp chứa nó chưa được quét, vì ý định đó đã là sai.
    /// </summary>
    [Fact]
    public void M10_khong_co_tool_xoa_sua_truc_tiep_hay_xuat_file()
    {
        RegisteredTools().Select(tool => tool.Name)
            .Should().NotContain(name => ForbiddenVerb().IsMatch(name));

        ToolMethods().Select(pair => pair.Name).Should().BeEquivalentTo(ExpectedTools);

        typeof(JsonPartnerStore).GetMethod(nameof(JsonPartnerStore.DeleteAsync))!
            .GetCustomAttribute<McpServerToolAttribute>().Should().BeNull();
    }

    private static IReadOnlyList<Tool> RegisteredTools()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpServer().WithToolsFromAssembly(WebAssembly);

        using var provider = services.BuildServiceProvider();

        return provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool).ToList();
    }

    /// <summary>Mọi phương thức mang <c>[McpServerTool]</c> trong assembly, bất kể lớp chứa có được quét hay không.</summary>
    private static IReadOnlyList<(string Name, MethodInfo Method)> ToolMethods() =>
        WebAssembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => (Attribute: method.GetCustomAttribute<McpServerToolAttribute>(), Method: method))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => (pair.Attribute!.Name ?? pair.Method.Name, pair.Method))
            .ToList();

    [GeneratedRegex("[ăâđêôơưạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹáàãéèíìóòõúùý]")]
    private static partial Regex VietnameseLetter();

    [GeneratedRegex("delete|remove|update|edit|export|csv|xoa|sua|xuat", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenVerb();
}
