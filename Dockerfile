# One image recipe for the sample's three hosts; PROJECT picks which one (see docker-compose.yml):
#   GM.FileStorage.Sample.API, GM.FileStorage.Sample.Worker or GM.FileStorage.Sample.Gateway.API
ARG PROJECT

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT/$PROJECT.csproj" -c Release -o /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV APP_DLL=${PROJECT}.dll
WORKDIR /app
COPY --from=build /app/publish .
# The local storage root the API and worker share (a named volume in docker-compose.yml). Created here, owned by the
# image's non-root user, so a fresh volume starts out writable.
RUN mkdir -p /data/file-storage && chown -R "$APP_UID" /data
USER $APP_UID
# Plain HTTP on 8080 inside the compose network; only the gateway is published.
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
