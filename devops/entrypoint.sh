#!/bin/bash
# First argument is name of env var, second is default value.
ensureEnvironmentVariableIsThere () {
    if [ -z "${!1}" ]; then
        echo "Environment Variable $1 not found, defaulting to $2"
        export $1=$2
    fi
}

ensureEnvironmentVariableIsThere "templateFilePath" "deployment.template.json"
ensureEnvironmentVariableIsThere "defaultPlatform" "amd64"

if [ $1 = "build" ]; then
    echo "Building iot edge module"
    sudo -E iotedgedev $1
elif [ $1 = "push" ]; then
    echo "Pushing iot edge module"
    sudo -E iotedgedev $1
elif [ $1 = "deploy" ]; then
    echo "Deploying iot edge module"
    sudo -E iotedgedev genconfig 
    sudo az extension add --name azure-iot
    sudo -E az iot edge deployment delete --login "$IOTHUB_CONNECTION_STRING" --deployment-id "$IOT_EDGE_DEPLOYMENT_ID"
    sudo -E az iot edge deployment create --login "$IOTHUB_CONNECTION_STRING" --content "config/deployment.json" --deployment-id "$IOT_EDGE_DEPLOYMENT_ID" --target-condition "deviceId='$DEVICE_ID'"
fi
