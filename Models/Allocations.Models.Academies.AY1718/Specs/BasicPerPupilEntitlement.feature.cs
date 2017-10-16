﻿// ------------------------------------------------------------------------------
//  <auto-generated>
//      This code was generated by SpecFlow (http://www.specflow.org/).
//      SpecFlow Version:2.2.0.0
//      SpecFlow Generator Version:2.2.0.0
// 
//      Changes to this file may cause incorrect behavior and will be lost if
//      the code is regenerated.
//  </auto-generated>
// ------------------------------------------------------------------------------
#region Designer generated code
#pragma warning disable
namespace AY1718.CSharp.Specs
{
    using TechTalk.SpecFlow;
    
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("TechTalk.SpecFlow", "2.2.0.0")]
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public partial class BasicPerPupilEntitlementFeature : Xunit.IClassFixture<BasicPerPupilEntitlementFeature.FixtureData>, System.IDisposable
    {
        
        private static TechTalk.SpecFlow.ITestRunner testRunner;
        
        private Xunit.Abstractions.ITestOutputHelper _testOutputHelper;
        
#line 1 "BasicPerPupilEntitlement.feature"
#line hidden
        
        public BasicPerPupilEntitlementFeature(BasicPerPupilEntitlementFeature.FixtureData fixtureData, Xunit.Abstractions.ITestOutputHelper testOutputHelper)
        {
            this._testOutputHelper = testOutputHelper;
            this.TestInitialize();
        }
        
        public static void FeatureSetup()
        {
            testRunner = TechTalk.SpecFlow.TestRunnerManager.GetTestRunner();
            TechTalk.SpecFlow.FeatureInfo featureInfo = new TechTalk.SpecFlow.FeatureInfo(new System.Globalization.CultureInfo("en-US"), "Basic Per Pupil Entitlement", "\tIn order to avoid silly mistakes\r\n\tAs a math idiot\r\n\tI want to be told the sum o" +
                    "f two numbers", ProgrammingLanguage.CSharp, ((string[])(null)));
            testRunner.OnFeatureStart(featureInfo);
        }
        
        public static void FeatureTearDown()
        {
            testRunner.OnFeatureEnd();
            testRunner = null;
        }
        
        public virtual void TestInitialize()
        {
        }
        
        public virtual void ScenarioTearDown()
        {
            testRunner.OnScenarioEnd();
        }
        
        public virtual void ScenarioSetup(TechTalk.SpecFlow.ScenarioInfo scenarioInfo)
        {
            testRunner.OnScenarioStart(scenarioInfo);
            testRunner.ScenarioContext.ScenarioContainer.RegisterInstanceAs<Xunit.Abstractions.ITestOutputHelper>(_testOutputHelper);
        }
        
        public virtual void ScenarioCleanup()
        {
            testRunner.CollectScenarioErrors();
        }
        
        public virtual void FeatureBackground()
        {
#line 6
#line 7
 testRunner.Given("I am using the \'SBS1718\' model", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Given ");
#line hidden
            TechTalk.SpecFlow.Table table1 = new TechTalk.SpecFlow.Table(new string[] {
                        "NOR_Pri_SBS",
                        "NOR_Pri_KS4_SBS"});
            table1.AddRow(new string[] {
                        "2780.00",
                        "24"});
#line 8
 testRunner.And("I have the following global variables:", ((string)(null)), table1, "And ");
#line hidden
        }
        
        void System.IDisposable.Dispose()
        {
            this.ScenarioTearDown();
        }
        
        [Xunit.FactAttribute(DisplayName="Basic Entitlement Primary Rates")]
        [Xunit.TraitAttribute("FeatureTitle", "Basic Per Pupil Entitlement")]
        [Xunit.TraitAttribute("Description", "Basic Entitlement Primary Rates")]
        [Xunit.TraitAttribute("Category", "mytag")]
        public virtual void BasicEntitlementPrimaryRates()
        {
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Basic Entitlement Primary Rates", new string[] {
                        "mytag"});
#line 13
this.ScenarioSetup(scenarioInfo);
#line 6
this.FeatureBackground();
#line hidden
            TechTalk.SpecFlow.Table table2 = new TechTalk.SpecFlow.Table(new string[] {
                        "URN",
                        "Date Opened",
                        "Local Authority"});
            table2.AddRow(new string[] {
                        "10027549",
                        "12/12/1980",
                        "Northumberland"});
#line 14
 testRunner.Given("I have the following \'APT Provider Information\' provider dataset:", ((string)(null)), table2, "Given ");
#line hidden
            TechTalk.SpecFlow.Table table3 = new TechTalk.SpecFlow.Table(new string[] {
                        "URN",
                        "Primary Amount Per Pupil",
                        "Primary Amount",
                        "Primary Notional SEN"});
            table3.AddRow(new string[] {
                        "10027549",
                        "2807.00",
                        "0.00",
                        "0.00"});
#line 18
 testRunner.And("I have the following \'APT Basic Entitlement\' provider dataset:", ((string)(null)), table3, "And ");
#line hidden
            TechTalk.SpecFlow.Table table4 = new TechTalk.SpecFlow.Table(new string[] {
                        "Local Authority",
                        "Phase",
                        "Local Authority"});
            table4.AddRow(new string[] {
                        "Northumberland",
                        "Primary",
                        "1243"});
#line 22
 testRunner.And("I have the following \'APT Local Authority\' provider dataset:", ((string)(null)), table4, "And ");
#line hidden
            TechTalk.SpecFlow.Table table5 = new TechTalk.SpecFlow.Table(new string[] {
                        "URN",
                        "Phase",
                        "Local Authority"});
            table5.AddRow(new string[] {
                        "10027549",
                        "2807.00",
                        "1243"});
#line 26
 testRunner.And("I have the following \'Census Weights\' provider dataset:", ((string)(null)), table5, "And ");
#line hidden
            TechTalk.SpecFlow.Table table6 = new TechTalk.SpecFlow.Table(new string[] {
                        "URN",
                        "NOR",
                        "NOR Primary",
                        "NOR Y1ToY2",
                        "NOR Y3ToY6"});
            table6.AddRow(new string[] {
                        "10027549",
                        "2807",
                        "1243",
                        "",
                        ""});
#line 30
 testRunner.And("I have the following \'Census Number Counts\' provider dataset:", ((string)(null)), table6, "And ");
#line 34
 testRunner.When("I calculate the allocations for the provider", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
            TechTalk.SpecFlow.Table table7 = new TechTalk.SpecFlow.Table(new string[] {
                        "UKPRN",
                        "P004_PriRate",
                        "P005_PriBESubtotal",
                        "P006_NSEN_PriBE",
                        "P006a_NSEN_PriBE_Percent"});
            table7.AddRow(new string[] {
                        "10027549",
                        "2807.00",
                        "3489101.00",
                        "0.00",
                        "0.00"});
#line 36
 testRunner.Then("the allocation statement should be:", ((string)(null)), table7, "Then ");
#line hidden
            this.ScenarioCleanup();
        }
        
        [System.CodeDom.Compiler.GeneratedCodeAttribute("TechTalk.SpecFlow", "2.2.0.0")]
        [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
        public class FixtureData : System.IDisposable
        {
            
            public FixtureData()
            {
                BasicPerPupilEntitlementFeature.FeatureSetup();
            }
            
            void System.IDisposable.Dispose()
            {
                BasicPerPupilEntitlementFeature.FeatureTearDown();
            }
        }
    }
}
#pragma warning restore
#endregion
