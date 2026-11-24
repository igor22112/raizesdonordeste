FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/PaymentGateway.Mock/PaymentGateway.Mock.csproj src/PaymentGateway.Mock/
RUN dotnet restore src/PaymentGateway.Mock/PaymentGateway.Mock.csproj
COPY src/PaymentGateway.Mock/ src/PaymentGateway.Mock/
RUN dotnet publish src/PaymentGateway.Mock/PaymentGateway.Mock.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PaymentGateway.Mock.dll"]
