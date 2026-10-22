# The SDK is a sibling checkout, so the build takes it as a named additional context
# (no parent-directory context needed):
#
#   docker build --build-context suwageval=../suwalski-agent-eval -t suwexag:local .
#
# Run it with the model settings from the environment:
#
#   docker run --rm -p 7010:7010 --env-file .env suwexag:local

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS builder

# The layout of the two checkouts side by side, so ExampleAgent.csproj's relative
# ProjectReference (../../suwalski-agent-eval/...) resolves as on the host.
WORKDIR /src/suwalski-agent-eval
COPY --from=suwageval global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY --from=suwageval Suwageval.AgentSdk/Suwageval.AgentSdk.csproj Suwageval.AgentSdk/packages.lock.json Suwageval.AgentSdk/

WORKDIR /src/suwalski-example-agent
# Manifests and lockfiles first: this layer only rebuilds when a dependency moves. Locked mode: a
# lockfile out of date with the packages fails the image.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY ExampleAgent/ExampleAgent.csproj ExampleAgent/packages.lock.json ExampleAgent/
RUN dotnet restore ExampleAgent --locked-mode

# Then the sources, which change every commit.
COPY --from=suwageval Suwageval.AgentSdk/ /src/suwalski-agent-eval/Suwageval.AgentSdk/
COPY .editorconfig ./
COPY ExampleAgent/ ExampleAgent/
RUN dotnet publish ExampleAgent --no-restore -c Release -o /app


# The runtime image matches the SDK band above; both are pinned.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble

COPY --from=builder /app /app

# In a container the agent listens on every interface (the host default in appsettings.json is localhost).
# (An app-configuration variable: appsettings.json's "Urls" would win over ASPNETCORE_URLS.)
ENV Urls=http://0.0.0.0:7010

# The image's own non-root user (uid 1654).
USER app
WORKDIR /app
EXPOSE 7010

ENTRYPOINT ["dotnet", "ExampleAgent.dll"]
