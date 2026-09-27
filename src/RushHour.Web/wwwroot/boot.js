Blazor.start().catch(error => {
    console.error(error);
    document.getElementById('blazor-error-ui').style.display = 'block';
    document.querySelector('.boot-text').textContent = 'ゲームを読み込めませんでした。';
});
