using NUnit.Framework;

namespace CargoStack.Tests
{
    /// <summary>
    /// 별점 규칙(기획 요청): 모두 옮기면 3개, 하나라도 잃으면 2개, 반 이상 잃으면 1개, 전부 잃으면 0개.
    /// 순수 계산이라 씬 로드 없이 EditMode 스타일 [Test] 로 검증한다.
    /// </summary>
    public class CargoTrackerStarRatingTests
    {
        [Test]
        public void 하나도_잃지_않으면_별_3개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 0), Is.EqualTo(3));
        }

        [Test]
        public void 하나만_잃어도_별_2개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 1), Is.EqualTo(2));
        }

        [Test]
        public void 반_미만으로_잃으면_별_2개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 4), Is.EqualTo(2));
        }

        [Test]
        public void 정확히_반을_잃으면_별_1개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 5), Is.EqualTo(1));
        }

        [Test]
        public void 반보다_많이_잃으면_별_1개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 8), Is.EqualTo(1));
        }

        [Test]
        public void 짐이_없으면_별_3개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 0, dropped: 0), Is.EqualTo(3));
        }

        [Test]
        public void 전부_잃으면_별_0개()
        {
            Assert.That(CargoTracker.CalculateStars(total: 10, dropped: 10), Is.EqualTo(0));
        }
    }
}
