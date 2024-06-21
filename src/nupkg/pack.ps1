# 获取当前目录路径
cd $PSScriptRoot

# 定义所有项目
$projects = (
	"Thea",
	"Thea.Alarm",
	"Thea.JwtToken",
	"Thea.Logging",
	"Thea.Logging.Alarm",
	"Thea.Logging.Template",
	"Thea.Logging.Alarm",
	"Thea.MessageDriven",
	"Thea.Job",
	"Thea.Web"
)
Remove-Item *.nupkg -recurse
cd ..

# 打包
foreach($project in $projects) {    
    cd $project
	dotnet clean
	dotnet build -c Release
    dotnet pack -c Release -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
    mv ./bin/Release/*.nupkg ../nupkg
    cd ..
}
pause