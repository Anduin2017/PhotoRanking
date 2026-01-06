ARG CSPROJ_PATH="./src/Anduin.PhotoRanking/"
ARG PROJ_NAME="Anduin.PhotoRanking"

# ============================
# Prepare Building Environment
FROM hub.aiursoft.com/aiursoft/internalimages/dotnet AS build-env
ARG CSPROJ_PATH
ARG PROJ_NAME

# curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | sudo gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg --yes
# NODE_MAJOR=24
# echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_$NODE_MAJOR.x nodistro main" | sudo tee /etc/apt/sources.list.d/nodesource.list
# sudo apt update
# sudo apt install nodejs

# Install nodejs for frontend building
RUN apt-get update && apt-get install -y curl gnupg && \
    mkdir -p /etc/apt/keyrings && \
    curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg --yes && \
    NODE_MAJOR=24 && \
    echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_$NODE_MAJOR.x nodistro main" | tee /etc/apt/sources.list.d/nodesource.list && \
    apt-get update && apt-get install -y nodejs
WORKDIR /src
COPY . .

# Build
RUN dotnet publish ${CSPROJ_PATH}${PROJ_NAME}.csproj  --configuration Release --no-self-contained --runtime linux-x64 --output /app
RUN cp -r ${CSPROJ_PATH}/wwwroot/* /app/wwwroot

# ============================
# Prepare Runtime Environment
FROM hub.aiursoft.com/aiursoft/internalimages/dotnet
ARG PROJ_NAME
WORKDIR /app
COPY --from=build-env /app .

# Edit appsettings.json
RUN sed -i 's/DataSource=app.db/DataSource=\/data\/app.db/g' appsettings.json
RUN sed -i 's/\/tmp\/data/\/data/g' appsettings.json
RUN mkdir -p /data

VOLUME /data
EXPOSE 5000

ENV SRC_SETTINGS=/app/appsettings.json
ENV VOL_SETTINGS=/data/appsettings.json
ENV DLL_NAME=${PROJ_NAME}.dll

#ENTRYPOINT dotnet $DLL_NAME --urls http://*:5000
ENTRYPOINT ["/bin/bash", "-c", "\
    if [ ! -f \"$VOL_SETTINGS\" ]; then \
        cp $SRC_SETTINGS $VOL_SETTINGS; \
    fi && \
    if [ -f \"$SRC_SETTINGS\" ]; then \
        rm $SRC_SETTINGS; \
    fi && \
    ln -s $VOL_SETTINGS $SRC_SETTINGS && \
    dotnet $DLL_NAME --urls http://*:5000 \
"]

HEALTHCHECK --interval=10s --timeout=3s --start-period=180s --retries=3 CMD \
wget --quiet --tries=1 --spider http://localhost:5000/health || exit 1