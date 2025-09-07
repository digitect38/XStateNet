# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY XStateNet5Impl/XStateNet5.csproj XStateNet5Impl/
COPY XStateNet.Distributed/XStateNet.Distributed.csproj XStateNet.Distributed/
COPY app/DistributedExample/DistributedExample.csproj app/DistributedExample/

# Restore dependencies
RUN dotnet restore app/DistributedExample/DistributedExample.csproj

# Copy everything else
COPY . .

# Build the application
RUN dotnet build app/DistributedExample/DistributedExample.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish app/DistributedExample/DistributedExample.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install necessary tools for debugging
RUN apt-get update && apt-get install -y \
    curl \
    net-tools \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose ports
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "DistributedExample.dll"]