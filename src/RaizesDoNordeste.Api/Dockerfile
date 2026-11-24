# Build multi-estágio: a imagem final contém só o runtime ASP.NET e a aplicação publicada.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/RaizesDoNordeste.Domain/RaizesDoNordeste.Domain.csproj src/RaizesDoNordeste.Domain/
COPY src/RaizesDoNordeste.Application/RaizesDoNordeste.Application.csproj src/RaizesDoNordeste.Application/
COPY src/RaizesDoNordeste.Infrastructure/RaizesDoNordeste.Infrastructure.csproj src/RaizesDoNordeste.Infrastructure/
COPY src/RaizesDoNordeste.Api/RaizesDoNordeste.Api.csproj src/RaizesDoNordeste.Api/
RUN dotnet restore src/RaizesDoNordeste.Api/RaizesDoNordeste.Api.csproj
COPY src/RaizesDoNordeste.Domain/ src/RaizesDoNordeste.Domain/
COPY src/RaizesDoNordeste.Application/ src/RaizesDoNordeste.Application/
COPY src/RaizesDoNordeste.Infrastructure/ src/RaizesDoNordeste.Infrastructure/
COPY src/RaizesDoNordeste.Api/ src/RaizesDoNordeste.Api/
RUN dotnet publish src/RaizesDoNordeste.Api/RaizesDoNordeste.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Usuário sem privilégios de root (já definido na imagem oficial).
USER $APP_UID
ENTRYPOINT ["dotnet", "RaizesDoNordeste.Api.dll"]
