FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY shop/Shop.csproj ./
RUN dotnet restore
COPY shop/ ./
RUN dotnet publish -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
COPY *.html *.css *.js *.jpg *.png *.xml *.txt ./wwwroot/
COPY assets/ ./wwwroot/assets/
COPY archive/ ./wwwroot/archive/
COPY logo/ ./wwwroot/logo/
COPY sons/ ./wwwroot/sons/
RUN sed -i 's/data-shop-host="static"/data-shop-host="railway"/' wwwroot/boutique.html
ENV PORT=8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shop.dll"]
