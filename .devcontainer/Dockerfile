FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

RUN apt update && \
    apt install -y --no-install-recommends \
        ca-certificates \
        gnupg \
        wget

# RUN sudo add-apt-repository multiverse && \
#     sudo dpkg --add-architecture i386 && \
#     sudo apt update && \
#     sudo apt install steamcmd

RUN mkdir -p /run/user/1000 && chown 1000:1000 -R /run/user/1000 