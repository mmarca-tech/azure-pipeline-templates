#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../src/ConfluencePublisher/ConfluencePublisher.csproj

using PipelineTools.ConfluencePublisher;

await ConfluenceDocsPublisher.RunAsync();
