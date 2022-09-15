FROM mcr.microsoft.com/dotnet/sdk:6.0

WORKDIR /pm
ADD . .

RUN [ "dotnet", "build", "-c", "Release" ]
RUN [ "dotnet", "test", "-c", "Release" ]

CMD [ "dotnet", "ProcessMonitor/bin/Release/net6.0/ProcessMonitor.dll", "--use-system-console" ]