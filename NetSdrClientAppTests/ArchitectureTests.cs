using System.Reflection;
using NetArchTest.Rules;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class ArchitectureTests
    {

        [Test]
        public void Messages_ShouldNot_Depend_On_Networking()
        {
            var result = Types
                .InAssembly(Assembly.Load("NetSdrClientApp"))
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Networking")
                .GetResult();

            Assert.That(
                result.IsSuccessful,
                Is.True,
                "Messages має заборонені залежності від Networking"
            );
        }

        [Test]
        public void Client_ShouldNot_Depend_On_Server()
        {
            var result = Types
                .InAssembly(Assembly.Load("NetSdrClientApp"))
                .ShouldNot()
                .HaveDependencyOn("EchoServer")
                .GetResult();

            Assert.That(
                result.IsSuccessful,
                Is.True, 
                "Продакшен-код не повинен залежати від тестового проєкту, але є залежності"
            );
        }
    }
}
