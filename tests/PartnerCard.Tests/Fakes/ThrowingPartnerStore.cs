using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Kho mà thao tác nào cũng ném <see cref="IOException"/> kèm đường dẫn file — ca M-09.
/// Thông điệp mang đường dẫn tuyệt đối để ca kiểm thử bắt được nếu nó lọt ra kết quả tool.
/// </summary>
public sealed class ThrowingPartnerStore : IPartnerStore
{
    public const string SecretDetail = @"Tiến trình khác đang giữ C:\Users\tayho\Code\data\partners.json";

    public Task<Partner?> GetAsync(string partnerId, CancellationToken ct) => throw Failure();

    public Task<IReadOnlyList<Partner>> SearchAsync(PartnerQuery query, CancellationToken ct) => throw Failure();

    public Task<Partner> UpsertAsync(Partner partner, CancellationToken ct) => throw Failure();

    public Task<bool> DeleteAsync(string partnerId, string reason, CancellationToken ct) => throw Failure();

    public Task<IReadOnlyList<Partner>> FindDuplicatesAsync(Partner candidate, CancellationToken ct) =>
        throw Failure();

    private static IOException Failure() => new(SecretDetail);
}
