const button =
    document.getElementById("ai-button");

const windowDiv =
    document.getElementById("ai-chat-window");

button.addEventListener("click", () => {

    if (windowDiv.style.display === "none")
        windowDiv.style.display = "block";
    else
        windowDiv.style.display = "none";
});