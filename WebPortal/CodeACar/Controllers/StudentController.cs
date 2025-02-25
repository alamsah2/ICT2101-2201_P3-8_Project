﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CodeACar.Models;
using CodeACar.Models.WebFormData;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace CodeACar.Controllers
{
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;

        //Constructor of the StudentController that initialises the database in the controller
        public StudentController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var studentId = int.Parse(HttpContext.Request.Cookies["studentId"].ToString());
            var role = HttpContext.Request.Cookies["role"].ToString();

            if (string.IsNullOrEmpty(role) || role != "Student")
            {
                return RedirectToAction("Login", "User");
            }
            else
            {
                ViewData["role"] = role.ToString();
                var challenges = _context.Challenges.ToList();
                ViewData["Challenges"] = challenges;
                ViewData["Histories"] = _context.ChallengeHistories.Where(c => c.UserId == studentId).ToList();
                return View("Index");
            }
        }

        [Route("/Student/PlayGame/{challengeId}")]
        public IActionResult PlayGame(int challengeId)
        {
            var role = HttpContext.Request.Cookies["role"].ToString();

            if (string.IsNullOrEmpty(role) || role != "Student")
            {
                return RedirectToAction("Login", "User");
            }
            else
            {
                ViewData["role"] = role.ToString();
                var challenge = _context.Challenges.FirstOrDefault(c => c.ChallengeId == challengeId);

                if (challenge == null)
                {
                    return RedirectToAction("Index", "Student"); // Redirect back to student's home page 
                }
                else
                {
                    return View("PlayGame", challenge);
                }
            }
        }

        [HttpPost]
        [Route("/Student/SendCarCommand/{challengeId}")]
        public async Task<IActionResult> SendCarCommand(int challengeId)
        {
            var challengeCmdResult = "true";
            var historyCount = 0; //locally store the challengeHistoryID to send to flask API
            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync();

                    var challengeHistoryInfo = JsonConvert.DeserializeObject<ChallengeHistory>(json);

                    var studentId = int.Parse(HttpContext.Request.Cookies["studentId"].ToString());
                    var student = _context.Users.FirstOrDefault(stud => stud.Id == studentId);

                    var challenge = _context.Challenges.FirstOrDefault(challenge => challenge.ChallengeId == challengeId);
                    var getAllChallengeHistory = _context.ChallengeHistories.ToList();

                    if (getAllChallengeHistory != null)
                    {
                        foreach (var item in getAllChallengeHistory)
                        {
                            historyCount = item.ChallengeHistoryId; //set historyCount as the latest challengeHistoryID
                        }
                    }

                    if (challenge == null)
                    {
                        return RedirectToAction("Index", "Student"); //If the challenge does not exist, redirect user back to the dashboard
                    }

                    var newChallengeHistory = new ChallengeHistory();
                    newChallengeHistory.ChallengeId = challenge.ChallengeId;
                    newChallengeHistory.Command = challengeHistoryInfo.Command;
                    newChallengeHistory.UserId = studentId;
                    newChallengeHistory.IsCompleted = challengeHistoryInfo.Command.TrimEnd() == challenge.Solution;// Assign whether student's solution match the challenge's solution to IsCompleted
                    challengeCmdResult = newChallengeHistory.IsCompleted.ToString().ToLower();

                    // Send the car command to flask server
                    string flaskServerApiUrl = "http://192.168.1.5/sendCarCommand";
                    var httpWebRequest = (HttpWebRequest)WebRequest.Create(flaskServerApiUrl);
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Method = "POST";

                    using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                    {
                        json = "{\"carCommand\":\"" + newChallengeHistory.Command + "\", \"challengeHistoryId\":\"" + (historyCount + 1) + "\"}";

                        streamWriter.Write(json);
                    }

                    var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse(); // sends out the http POST request

                    // Once command sent successfully and response received, add Challenge History into Database
                    _context.ChallengeHistories.Add(newChallengeHistory);
                    _context.SaveChanges();

                    // Send success message to student
                    var responseObj = new { message = "Successfully sent command to the car!", challengeCommandResult = challengeCmdResult };
                    return Ok(responseObj); // Send success message to the user
                }
            }
            catch (Exception ex)
            {
                var errorResponseObj = new { message = "Something went wrong when sending the commands to the car! Please contact the system administrator.", challengeCommandResult = challengeCmdResult };
                return BadRequest(errorResponseObj);
            }
        }

        [HttpPost]
        [Route("/Student/UpdateCarStatistic")]
        public async Task<IActionResult> UpdateCarStatistic()
        {
            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync();
                    var statistic = JsonConvert.DeserializeObject<CarStatisticJson>(json);

                    ChallengeHistory currentHistory = _context.ChallengeHistories.FirstOrDefault(h => h.ChallengeHistoryId == statistic.ChallengeHistoryId);

                    if (currentHistory == null)
                    {
                        return BadRequest();
                    }

                    currentHistory.Statistics = json;
                    _context.ChallengeHistories.Update(currentHistory);
                    _context.SaveChanges();

                    return Ok();
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.StackTrace);
            }
        }
    }
}
