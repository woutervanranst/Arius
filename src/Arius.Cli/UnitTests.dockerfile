# See https://learn.microsoft.com/en-us/visualstudio/test/remote-testing?view=vs-2022

# When updating the Dockerfile: 
# 1. Delete the old container
# 2. Build the project
# 3. Swich from Test Environment to 'local' and back

FROM mcr.microsoft.com/dotnet/sdk:7.0

RUN wget https://aka.ms/getvsdbgsh && \
    sh getvsdbgsh -v latest  -l /vsdbg

VOLUME /archive
VOLUME /logs