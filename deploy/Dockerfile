# Image chỉ chứa bản publish đã dựng sẵn trên máy phát triển, chép từ deploy/publish/.
# Không build trong cloud: global.json ghim SDK 10.0.400, image SDK trên cloud chưa chắc khớp.
#
# Container nghe cổng 8080 — image nền aspnet tự đặt biến ASPNETCORE_HTTP_PORTS, file này
# không đặt cổng nào.
#
# Không có khoá API hay biến môi trường nào trong file này. Cấu hình (GEMINI_API_KEY,
# PartnerCard__Extractor, PartnerCard__DataDirectory, PartnerCard__LogsDirectory) đặt ở
# service lúc deploy, không bao giờ nằm trong image.

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "PartnerCard.Web.dll"]
