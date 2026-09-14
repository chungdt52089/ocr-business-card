using Microsoft.Extensions.Options;
using PartnerCard.Web.Configuration;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Đọc cấu hình là ném — ca M-09.
///
/// Chọn đúng chỗ này vì <c>CardPipeline.ExtractAsync</c> đọc <c>MaxImageBytes</c> **trước** khối
/// <c>try</c> của nó: đây là một exception thật sự thoát ra khỏi đường ống, dựng được mà không phải
/// sửa đường ống. Thông điệp cố ý mang đủ ba thứ SPEC mục 10.3 cấm lộ ra ngoài.
/// </summary>
public sealed class ThrowingOptions : IOptions<PartnerCardOptions>
{
    public const string SecretDetail =
        @"Không đọc được C:\Users\tayho\Code\src\PartnerCard.Web\appsettings.Development.json: thiếu GEMINI_API_KEY";

    public PartnerCardOptions Value => throw new InvalidOperationException(SecretDetail);
}
