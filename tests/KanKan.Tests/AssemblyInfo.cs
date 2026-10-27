// WPF의 Application.LoadComponent(창 XAML 불러오기)는 여러 스레드에서 동시에 부르면 깨진다.
// 테스트마다 STA 스레드를 따로 띄우므로, 테스트 클래스끼리 동시에 돌지 않게 한다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
