FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/DoubleMark.Api/DoubleMark.Api.csproj", "src/DoubleMark.Api/"]
RUN dotnet restore "src/DoubleMark.Api/DoubleMark.Api.csproj"
COPY src/DoubleMark.Api/ src/DoubleMark.Api/
RUN dotnet publish "src/DoubleMark.Api/DoubleMark.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DoubleMark.Api.dll"]
