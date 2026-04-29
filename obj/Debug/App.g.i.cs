namespace PastelIntegrator
{
    public static class Program
    {
        [System.STAThreadAttribute()]
        public static void Main()
{
    var app = new PastelIntegrator.App();
            app.InitializeComponent();
            app.Run();
        }
    }
}