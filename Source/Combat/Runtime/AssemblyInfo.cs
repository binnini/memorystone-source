using System.Runtime.CompilerServices;

// Combat/Runtime의 internal 멤버는 분리 전 단일 SeoulPlayup.Combat 어셈블리에서
// Combat/Unity 표현 레이어와 EditMode 테스트가 직접 접근하던 것이다. 어셈블리
// 분리 후에도 동일 접근을 보존하기 위해 InternalsVisibleTo를 부여한다.
[assembly: InternalsVisibleTo("SeoulPlayup.Combat")]
[assembly: InternalsVisibleTo("SeoulPlayup.Combat.EditModeTests")]
