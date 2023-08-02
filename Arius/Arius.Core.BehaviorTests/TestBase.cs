﻿using Arius.Core.Repositories;

namespace Arius.Core.BehaviorTests;

[Binding]
class TestBase
{
    public TestBase(ScenarioContext sc)
    {
        scenarioContext = sc;
    }

    protected readonly ScenarioContext scenarioContext;

    protected Repository Repository => TestSetup.Repository;
}