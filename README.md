# C# Dotnet Runtime Package Store CLI Extention

### Motivation
I've been experimenting with AWS Lambda Layers lately in an attempt to reduce overall cold start times associated with lambdas written in C#. AWS provides an extention library full of utilities to generate packages and automate deployments for several AWS services, which can be found [here](https://github.com/aws/aws-extensions-for-dotnet-cli). 

After some initial investigation into using the AWS Extensions for .NET CLI in my CI/CD pipeline, I determined that generation of the lambda layer was coupled to deployment to S3. In my specific case, I needed to maintain the separation of build artifacts from deployment, so this was counterintuitive to the existing pipeline functionality. 

This project is heavily derived (**heavily**) from the [AWS Extensions for .NET CLI](https://github.com/aws/aws-extensions-for-dotnet-cli), enabling the user to build a new runtime package store from an existing project without being made to deploy the code to S3. Instead, the package store is saved locally.

##### Alternatives you may consider
A pattern I have observed is for developers to use two .csproj files, one for the project and one for the package store. This works natively with [dotnet store](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-store). This tool will derive a runtime package store from an existing project without the need to maintain a separate .csproj manifest. Note that the native .NET CLI provides no functionality to derive a runtime package manifest from an existing project. 

You may also be interested in this [related discussion](https://github.com/aws/aws-extensions-for-dotnet-cli/issues/79) on the AWS Extensions for .NET CLI project page.

### Build from source

Checkout this repository and run the following commands from inside the project directory: 

```
dotnet pack

dotnet tool install --global --add-source ./nupkg PkgStoreGen 
```

The command will then be installed and can be executed as a global tool:

```
pkgstoregen 
```

### Usage

#### Running the tool:

To build a new runtime package store from an existing project, invoke the _pkgstoregen create-local-layer_ command and pass in the project manifest, as well as the target framework. 

```
pkgstoregen create-local-layer --manifest .\myproject.csproj --framework netcoreapp2.1 --layername mylayer 
```

The output of this command is a link to the compiled and zipped runtime package store. In the case of an AWS Lambda deployment, this file can be used as the Lambda layer source. Note: The parent project also will need to be compiled against the manifest produced by this command to produce a deployment artifact that does not bundle the dependencies and is compatible with the package store. 

##### Building a parent project against the generated runtime package store manifest:

In this case, a developer has created a runtime package store from an existing project (say, a .NET MVC web project) and wishes to compile the existing project against the package store, effectively compiling the project without the extra dependencies. This is supported natively by the .NET CLI (see [dotnet publish](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)). Assuming the developer is in the project folder (local to the main project's .csproj file), and wishes to use the runtime package store manifest named **_artifact.xml_**.

```
dotnet publish --manifest .\artifact.xml
```

