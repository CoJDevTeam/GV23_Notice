document.getElementById("mValueView").disabled = true; 
document.getElementById("mvComment_main").disabled = true;
document.getElementById("cat_revised_drop").disabled = true;
document.getElementById("cat_comment_main").disabled = true;
document.getElementById("edit_delete_Main").disabled = true;

function disableBtn() {
    document.getElementById("mValueView").disabled = true;
    document.getElementById("mvComment_main").disabled = true;
    document.getElementById("status").value = "False";
}
function enableBtn() {
    document.getElementById("mValueView").disabled = false;
    document.getElementById("mvComment_main").disabled = false;
    document.getElementById("status").value = "True";
}

function disable_Cat() {
    document.getElementById("cat_revised_drop").disabled = true;
    document.getElementById("cat_comment_main").disabled = true;
    document.getElementById("edit_cat").value = "False";
}
function enable_Cat() {
    document.getElementById("cat_revised_drop").disabled = false;
    document.getElementById("cat_comment_main").disabled = false;
    document.getElementById("edit_cat").value = "True";
}
function disable_delete() {
    document.getElementById("edit_delete_Main").disabled = true;
    document.getElementById("status_delete").value = "False";
}
function enable_delete() {
    document.getElementById("edit_delete_Main").disabled = false;
    document.getElementById("status_delete").value = "True";
}