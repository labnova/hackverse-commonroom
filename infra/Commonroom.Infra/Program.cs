using Amazon.CDK;
using Commonroom.Infra;

var app = new App();

_ = new CommonroomAuthStack(app, "CommonroomAuthStack", new StackProps
{
    Env = new Amazon.CDK.Environment
    {
        Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
        Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
    },
    Description = "Commonroom Chat - Cognito User Pool with one public app client per project",
});

app.Synth();
