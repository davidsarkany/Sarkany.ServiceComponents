# Builds one sample service from the repository sources. Build context: repository root.
#   docker build -f samples/Dockerfile --build-arg PROJECT=Orders.Service .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /repo
COPY . .
RUN dotnet publish samples/${PROJECT}/${PROJECT}.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV PROJECT=${PROJECT}
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet ${PROJECT}.dll \"$@\"", "--"]
