# One image recipe for the sample's three hosts; PROJECT picks which one (see docker-compose.yml):
#   GM.FileStorage.Sample.API, GM.FileStorage.Sample.Worker or GM.FileStorage.Sample.Gateway.API
ARG PROJECT

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
# With its native launcher (apphost), so the container starts the app directly, not through a shell.
RUN dotnet publish "$PROJECT/$PROJECT.csproj" -c Release -o /app/publish -p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
# Npgsql looks for the Kerberos (GSSAPI) library when it connects; without it every start logs an error.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# A fixed name for whichever host this image runs. Started directly rather than via `sh -c`: the shell drops
# environment variables whose names aren't shell identifiers, such as
# ReverseProxy__Clusters__files-api__Destinations__primary__Address.
RUN ln -s "/app/$PROJECT" /app/entrypoint
# The local storage root the API and worker share (a named volume in docker-compose.yml). Created here, owned by the
# image's non-root user, so a fresh volume starts out writable.
RUN mkdir -p /data/file-storage && chown -R "$APP_UID" /data
USER $APP_UID
# Plain HTTP on 8080 inside the compose network; only the gateway is published.
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["/app/entrypoint"]
